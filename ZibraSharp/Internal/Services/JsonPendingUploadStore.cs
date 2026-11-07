using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Ports;

namespace ZibraSharp.Internal.Services
{
    /// <summary>
    /// JSON-backed store for archives that could not be uploaded.
    /// The file contains a simple array of absolute archive paths.
    /// </summary>
    public sealed class JsonPendingUploadStore : IPendingUploadStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly string _filePath;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public JsonPendingUploadStore(string filePath)
        {
            _filePath = Path.GetFullPath(filePath ?? throw new ArgumentNullException(nameof(filePath)));
        }

        public async Task<IReadOnlyList<string>> GetAllAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await LoadAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task AddAsync(string archivePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                throw new ArgumentException("Archive path must not be empty.", nameof(archivePath));
            }

            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var paths = (await LoadAsync(ct).ConfigureAwait(false)).ToList();
                if (!paths.Contains(archivePath, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(archivePath);
                    await SaveAsync(paths, ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task RemoveAsync(string archivePath, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var paths = (await LoadAsync(ct).ConfigureAwait(false))
                    .Where(path => !string.Equals(path, archivePath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                await SaveAsync(paths, ct).ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<IReadOnlyList<string>> LoadAsync(CancellationToken ct)
        {
            if (!File.Exists(_filePath))
            {
                return Array.Empty<string>();
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

                var paths = await JsonSerializer
                    .DeserializeAsync<List<string>>(stream, JsonOptions, ct)
                    .ConfigureAwait(false);

                return paths ?? new List<string>();
            }
            catch (JsonException)
            {
                // A corrupted queue must not block the application.
                return Array.Empty<string>();
            }
        }

        private async Task SaveAsync(IReadOnlyList<string> paths, CancellationToken ct)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = new FileStream(
                _filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous);

            await JsonSerializer.SerializeAsync(stream, paths, JsonOptions, ct).ConfigureAwait(false);
        }
    }
}
