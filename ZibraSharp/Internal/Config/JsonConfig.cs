using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Exceptions;
using ZibraSharp.Internal.IO;
using ZibraSharp.Internal.Ports;

namespace ZibraSharp.Internal.Config
{
    /// <summary>
    /// JSON-backed configuration:
    /// <list type="bullet">
    /// <item><description>zipdir.json — backup directory.</description></item>
    /// <item><description>extensions.json — extensions excluded from scanning.</description></item>
    /// </list>
    /// </summary>
    public sealed class JsonConfig : IConfig
    {
        public const string DefaultZipDirFileName = "zipdir.json";
        public const string DefaultExtensionsFileName = "extensions.json";

        // The original ZibraGo used the misspelled name; read it as a fallback.
        private const string LegacyExtensionsFileName = "extentions.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private readonly string _zipDirPath;
        private readonly string _extensionsPath;
        private readonly string _legacyExtensionsPath;

        public JsonConfig(
            string? baseDirectory = null,
            string zipDirFileName = DefaultZipDirFileName,
            string extensionsFileName = DefaultExtensionsFileName)
        {
            var root = Path.GetFullPath(baseDirectory ?? Directory.GetCurrentDirectory());
            _zipDirPath = Path.Combine(root, zipDirFileName);
            _extensionsPath = Path.Combine(root, extensionsFileName);
            _legacyExtensionsPath = Path.Combine(root, LegacyExtensionsFileName);
        }

        public async Task<ZipDirConfig?> GetDirAsync(CancellationToken ct = default)
        {
            if (!File.Exists(_zipDirPath))
            {
                return null;
            }

            try
            {
                await using var stream = OpenRead(_zipDirPath);
                return await JsonSerializer
                    .DeserializeAsync<ZipDirConfig>(stream, JsonOptions, ct)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                throw new AppException(
                    AppErrorCode.Parse,
                    nameof(GetDirAsync),
                    $"Failed to parse configuration file: {_zipDirPath}",
                    ex);
            }
            catch (IOException ex)
            {
                throw new AppException(
                    AppErrorCode.Open,
                    nameof(GetDirAsync),
                    $"Failed to read configuration file: {_zipDirPath}",
                    ex);
            }
        }

        public async Task SetZipDirAsync(string path, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new AppException(
                    AppErrorCode.Config,
                    nameof(SetZipDirAsync),
                    "Backup directory must not be empty.");
            }

            var config = new ZipDirConfig(path);
            await WriteJsonAsync(_zipDirPath, config, ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlySet<string>> GetExtensionsAsync(CancellationToken ct = default)
        {
            var path = File.Exists(_extensionsPath)
                ? _extensionsPath
                : File.Exists(_legacyExtensionsPath) ? _legacyExtensionsPath : null;

            if (path is null)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                await using var stream = OpenRead(path);
                var values = await JsonSerializer
                    .DeserializeAsync<Dictionary<string, bool>>(stream, JsonOptions, ct)
                    .ConfigureAwait(false);

                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (values is not null)
                {
                    foreach (var (extension, enabled) in values)
                    {
                        if (enabled && !string.IsNullOrWhiteSpace(extension))
                        {
                            result.Add(extension);
                        }
                    }
                }

                return result;
            }
            catch (JsonException ex)
            {
                throw new AppException(
                    AppErrorCode.Parse,
                    nameof(GetExtensionsAsync),
                    $"Failed to parse extensions file: {path}",
                    ex);
            }
            catch (IOException ex)
            {
                throw new AppException(
                    AppErrorCode.Open,
                    nameof(GetExtensionsAsync),
                    $"Failed to read extensions file: {path}",
                    ex);
            }
        }

        public async Task<IReadOnlySet<string>> CreateExtensionsAsync(string input, CancellationToken ct = default)
        {
            var extensions = ParseExtensions(input);
            var values = extensions.ToDictionary(extension => extension, _ => true);
            await WriteJsonAsync(_extensionsPath, values, ct).ConfigureAwait(false);
            return extensions;
        }

        public Task RemoveExtensionsFileAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (File.Exists(_extensionsPath))
            {
                File.Delete(_extensionsPath);
            }

            if (File.Exists(_legacyExtensionsPath))
            {
                File.Delete(_legacyExtensionsPath);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Parses input like ".exe .txt .json" into a set of extensions.
        /// Commas are rejected on purpose (the original tool required spaces).
        /// </summary>
        public static HashSet<string> ParseExtensions(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                throw new AppException(
                    AppErrorCode.Config,
                    nameof(ParseExtensions),
                    "User input is empty.");
            }

            if (input.Contains(','))
            {
                throw new AppException(
                    AppErrorCode.Config,
                    nameof(ParseExtensions),
                    "Use spaces between extensions: .exe .txt .json");
            }

            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            foreach (var item in items)
            {
                var dotIndex = item.IndexOf('.');
                if (dotIndex < 0)
                {
                    throw new AppException(
                        AppErrorCode.Config,
                        nameof(ParseExtensions),
                        $"Not an extension: {item}");
                }

                var extension = item[dotIndex..];
                if (extension.Length <= 1)
                {
                    throw new AppException(
                        AppErrorCode.Config,
                        nameof(ParseExtensions),
                        $"Not an extension: {item}");
                }

                result.Add(extension);
            }

            return result;
        }

        private static FileStream OpenRead(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        }

        private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
        {
            try
            {
                await AtomicFile.WriteAsync(
                    path,
                    (stream, token) => JsonSerializer.SerializeAsync(stream, value, JsonOptions, token),
                    ct).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new AppException(
                    AppErrorCode.Create,
                    nameof(WriteJsonAsync),
                    $"Failed to write configuration file: {path}",
                    ex);
            }
        }
    }
}
