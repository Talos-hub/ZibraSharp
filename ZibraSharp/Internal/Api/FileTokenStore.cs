using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Util.Store;
using ZibraSharp.Internal.IO;

namespace ZibraSharp.Internal.Api
{
    /// <summary>
    /// Stores the OAuth token in a single JSON file (token.json by default).
    /// </summary>
    public sealed class FileTokenStore : IDataStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly string _filePath;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public FileTokenStore(string filePath)
        {
            _filePath = Path.GetFullPath(filePath ?? throw new ArgumentNullException(nameof(filePath)));
        }

        public async Task StoreAsync<T>(string key, T value)
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                await AtomicFile.WriteAsync(
                    _filePath,
                    (stream, _) => JsonSerializer.SerializeAsync(stream, value, JsonOptions))
                    .ConfigureAwait(false);

                RestrictPermissions();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<T> GetAsync<T>(string key)
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!File.Exists(_filePath))
                {
                    return default!;
                }

                try
                {
                    await using var stream = new FileStream(
                        _filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        4096,
                        FileOptions.Asynchronous);

                    return await JsonSerializer
                        .DeserializeAsync<T>(stream, JsonOptions)
                        .ConfigureAwait(false)
                        ?? default!;
                }
                catch (JsonException)
                {
                    // A corrupted token must force a re-authentication, not crash the app.
                    return default!;
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task DeleteAsync<T>(string key)
        {
            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task ClearAsync()
        {
            await DeleteAsync<object>("").ConfigureAwait(false);
        }

        private void RestrictPermissions()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                // OAuth tokens are secrets: owner read/write only.
                File.SetUnixFileMode(
                    _filePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (IOException)
            {
                // Best effort; the token is still usable.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort; the token is still usable.
            }
        }
    }
}
