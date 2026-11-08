using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using ZibraSharp.Internal.Ports;

namespace ZibraSharp.Internal.Logger
{
    /// <summary>
    /// Log levels ordered by importance.
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warn = 2,
        Error = 3
    }

    /// <summary>
    /// Structured JSON-lines logger. Each event is written as one JSON object:
    /// <code>{"time":"...","level":"INFO","msg":"Files found","Count":3}</code>
    /// Arguments are treated as key/value pairs (like Go's slog); if they cannot
    /// be paired, they are written into an "args" array.
    /// </summary>
    public sealed class JsonLogger : ILogger, IDisposable
    {
        public const string DefaultFileName = "zibra.log";
        public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024;
        public const int DefaultMaxFiles = 5;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        private readonly object _sync = new();
        private readonly string _filePath;
        private readonly LogLevel _minimumLevel;
        private readonly TextWriter? _consoleWriter;
        private readonly long _maxFileSizeBytes;
        private readonly int _maxFiles;
        private StreamWriter? _writer;
        private long _currentSize;
        private bool _disposed;

        /// <param name="filePath">Path to the JSON-lines log file. Parent directories are created automatically.</param>
        /// <param name="minimumLevel">Events below this level are ignored.</param>
        /// <param name="consoleWriter">When not null, every event is mirrored to this writer (for example <see cref="Console.Out"/>).</param>
        /// <param name="maxFileSizeBytes">Rotate the log when it exceeds this size. 0 or less disables rotation.</param>
        /// <param name="maxFiles">Number of rotated files to keep (zibra.1.log … zibra.N.log).</param>
        public JsonLogger(
            string filePath,
            LogLevel minimumLevel = LogLevel.Info,
            TextWriter? consoleWriter = null,
            long maxFileSizeBytes = DefaultMaxFileSizeBytes,
            int maxFiles = DefaultMaxFiles)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Log file path must not be empty.", nameof(filePath));
            }

            if (maxFiles < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFiles), "At least one rotated file must be kept.");
            }

            _filePath = Path.GetFullPath(filePath);
            _minimumLevel = minimumLevel;
            _consoleWriter = consoleWriter;
            _maxFileSizeBytes = maxFileSizeBytes;
            _maxFiles = maxFiles;
        }

        public void Debug(string message, params object?[] args) => Write(LogLevel.Debug, message, args, null);

        public void Info(string message, params object?[] args) => Write(LogLevel.Info, message, args, null);

        public void Warn(string message, params object?[] args) => Write(LogLevel.Warn, message, args, null);

        public void Error(string message, params object?[] args) => Write(LogLevel.Error, message, args, null);

        public void Error(Exception exception, string message, params object?[] args) =>
            Write(LogLevel.Error, message, args, exception);

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _writer?.Dispose();
            }
        }

        private void Write(LogLevel level, string message, object?[] args, Exception? exception)
        {
            if (level < _minimumLevel || string.IsNullOrEmpty(message))
            {
                return;
            }

            var line = BuildLine(level, message, args, exception);

            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _writer ??= CreateWriter();

                if (ShouldRotate(line))
                {
                    Rotate();
                }

                _writer.WriteLine(line);
                _currentSize += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
                _consoleWriter?.WriteLine(line);
            }
        }

        private bool ShouldRotate(string line)
        {
            if (_maxFileSizeBytes <= 0 || _currentSize == 0)
            {
                return false;
            }

            var lineSize = Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
            return _currentSize + lineSize > _maxFileSizeBytes;
        }

        private void Rotate()
        {
            _writer?.Dispose();
            _writer = null;
            _currentSize = 0;

            // Drop the oldest file, then shift zibra.N.log → zibra.N+1.log.
            var oldest = GetRotatedPath(_maxFiles);
            TryDeleteFile(oldest);

            for (var index = _maxFiles - 1; index >= 1; index--)
            {
                var source = GetRotatedPath(index);
                var destination = GetRotatedPath(index + 1);
                if (File.Exists(source))
                {
                    File.Move(source, destination, overwrite: true);
                }
            }

            if (File.Exists(_filePath))
            {
                File.Move(_filePath, GetRotatedPath(1), overwrite: true);
            }

            _writer = CreateWriter();
        }

        private string GetRotatedPath(int index)
        {
            var directory = Path.GetDirectoryName(_filePath) ?? ".";
            var name = Path.GetFileNameWithoutExtension(_filePath);
            var extension = Path.GetExtension(_filePath);
            return Path.Combine(directory, $"{name}.{index}{extension}");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort cleanup.
            }
        }

        private StreamWriter CreateWriter()
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stream = new FileStream(
                _filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.None);

            _currentSize = stream.Length;

            return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
        }

        private static string BuildLine(LogLevel level, string message, object?[] args, Exception? exception)
        {
            var root = new JsonObject
            {
                ["time"] = DateTimeOffset.UtcNow.ToString("O"),
                ["level"] = level.ToString().ToUpperInvariant(),
                ["msg"] = message
            };

            AddArgs(root, args);

            if (exception is not null)
            {
                root["error"] = ExceptionToNode(exception);
            }

            return root.ToJsonString(JsonOptions);
        }

        private static void AddArgs(JsonObject root, object?[] args)
        {
            if (args.Length == 0)
            {
                return;
            }

            var isKeyValue = args.Length % 2 == 0;
            if (isKeyValue)
            {
                for (var i = 0; i < args.Length; i += 2)
                {
                    if (args[i] is not string key || string.IsNullOrWhiteSpace(key))
                    {
                        isKeyValue = false;
                        break;
                    }
                }
            }

            if (isKeyValue)
            {
                for (var i = 0; i < args.Length; i += 2)
                {
                    root[(string)args[i]!] = ToNode(args[i + 1]);
                }

                return;
            }

            var array = new JsonArray();
            foreach (var arg in args)
            {
                array.Add(ToNode(arg));
            }

            root["args"] = array;
        }

        private static JsonNode? ToNode(object? value)
        {
            return value switch
            {
                null => null,
                Exception exception => ExceptionToNode(exception),
                JsonNode node => node,
                _ => JsonSerializer.SerializeToNode(value, JsonOptions)
            };
        }

        private static JsonObject ExceptionToNode(Exception exception)
        {
            var node = new JsonObject
            {
                ["type"] = exception.GetType().FullName,
                ["message"] = exception.Message
            };

            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                node["stackTrace"] = exception.StackTrace;
            }

            if (exception.InnerException is not null)
            {
                node["inner"] = ExceptionToNode(exception.InnerException);
            }

            return node;
        }
    }
}
