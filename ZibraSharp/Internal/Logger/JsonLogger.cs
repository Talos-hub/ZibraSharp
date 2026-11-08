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

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        private readonly object _sync = new();
        private readonly StreamWriter _writer;
        private readonly LogLevel _minimumLevel;
        private readonly TextWriter? _consoleWriter;
        private bool _disposed;

        /// <param name="filePath">Path to the JSON-lines log file. Parent directories are created automatically.</param>
        /// <param name="minimumLevel">Events below this level are ignored.</param>
        /// <param name="consoleWriter">When not null, every event is mirrored to this writer (for example <see cref="Console.Out"/>).</param>
        public JsonLogger(
            string filePath,
            LogLevel minimumLevel = LogLevel.Info,
            TextWriter? consoleWriter = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Log file path must not be empty.", nameof(filePath));
            }

            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stream = new FileStream(
                fullPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.None);

            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };

            _minimumLevel = minimumLevel;
            _consoleWriter = consoleWriter;
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
                _writer.Dispose();
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

                _writer.WriteLine(line);
                _consoleWriter?.WriteLine(line);
            }
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
