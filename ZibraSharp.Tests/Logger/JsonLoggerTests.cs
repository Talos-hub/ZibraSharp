using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ZibraSharp.Internal.Logger;
using ZibraSharp.Tests.TestSupport;

namespace ZibraSharp.Tests.Logging
{
    public sealed class JsonLoggerTests
    {
        [Fact]
        public void Info_WritesStructuredJsonLine()
        {
            using var temp = new TempDirectory();
            var logPath = Path.Combine(temp.Path, "logs", "zibra.log");

            using (var logger = new JsonLogger(logPath, LogLevel.Debug))
            {
                logger.Info("Files found", "Count", 3);
            }

            var line = Assert.Single(File.ReadAllLines(logPath));
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            Assert.Equal("INFO", root.GetProperty("level").GetString());
            Assert.Equal("Files found", root.GetProperty("msg").GetString());
            Assert.Equal(3, root.GetProperty("Count").GetInt32());
            Assert.True(DateTimeOffset.TryParse(root.GetProperty("time").GetString(), out _));
        }

        [Fact]
        public void MinimumLevel_FiltersLessImportantEvents()
        {
            using var temp = new TempDirectory();
            var logPath = Path.Combine(temp.Path, "zibra.log");

            using (var logger = new JsonLogger(logPath, LogLevel.Warn))
            {
                logger.Debug("debug");
                logger.Info("info");
                logger.Warn("warn");
                logger.Error("error");
            }

            var lines = File.ReadAllLines(logPath);
            Assert.Equal(2, lines.Length);
            Assert.Contains(lines, line => line.Contains("\"WARN\""));
            Assert.Contains(lines, line => line.Contains("\"ERROR\""));
        }

        [Fact]
        public void Error_WithException_SerializesExceptionTree()
        {
            using var temp = new TempDirectory();
            var logPath = Path.Combine(temp.Path, "zibra.log");
            var exception = new InvalidOperationException(
                "outer",
                new IOException("inner"));

            using (var logger = new JsonLogger(logPath, LogLevel.Debug))
            {
                logger.Error(exception, "Upload failed", "Path", "backup.zip");
            }

            var line = Assert.Single(File.ReadAllLines(logPath));
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            Assert.Equal("backup.zip", root.GetProperty("Path").GetString());
            Assert.Equal("System.InvalidOperationException", root.GetProperty("error").GetProperty("type").GetString());
            Assert.Equal("outer", root.GetProperty("error").GetProperty("message").GetString());
            Assert.Equal("inner", root.GetProperty("error").GetProperty("inner").GetProperty("message").GetString());
        }

        [Fact]
        public void OddArguments_AreWrittenAsArray()
        {
            using var temp = new TempDirectory();
            var logPath = Path.Combine(temp.Path, "zibra.log");

            using (var logger = new JsonLogger(logPath, LogLevel.Debug))
            {
                logger.Info("message", "one", 2, "three");
            }

            var line = Assert.Single(File.ReadAllLines(logPath));
            using var document = JsonDocument.Parse(line);
            var args = document.RootElement.GetProperty("args");

            Assert.Equal(JsonValueKind.Array, args.ValueKind);
            Assert.Equal(3, args.GetArrayLength());
        }

        [Fact]
        public void EvenArgumentsWithNonStringKey_AreWrittenAsArray()
        {
            using var temp = new TempDirectory();
            var logPath = Path.Combine(temp.Path, "zibra.log");

            using (var logger = new JsonLogger(logPath, LogLevel.Debug))
            {
                logger.Info("message", 1, "value");
            }

            var line = Assert.Single(File.ReadAllLines(logPath));
            using var document = JsonDocument.Parse(line);

            Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("args").ValueKind);
        }

        [Fact]
        public void ConsoleMirror_WritesToProvidedWriter()
        {
            using var temp = new TempDirectory();
            var logPath = Path.Combine(temp.Path, "zibra.log");
            using var console = new StringWriter();

            using (var logger = new JsonLogger(logPath, LogLevel.Debug, console))
            {
                logger.Warn("Careful", "Code", 42);
            }

            var output = console.ToString();
            Assert.Contains("\"msg\":\"Careful\"", output);
            Assert.Contains("\"Code\":42", output);
        }

        [Fact]
        public async Task ParallelWrites_ProduceOneValidJsonLinePerEvent()
        {
            using var temp = new TempDirectory();
            var logPath = Path.Combine(temp.Path, "zibra.log");

            using (var logger = new JsonLogger(logPath, LogLevel.Debug))
            {
                await Task.Run(() =>
                {
                    Parallel.For(0, 200, index =>
                    {
                        logger.Info("event", "Index", index);
                    });
                });
            }

            var lines = File.ReadAllLines(logPath);
            Assert.Equal(200, lines.Length);

            var indices = new HashSet<int>();
            foreach (var line in lines)
            {
                using var document = JsonDocument.Parse(line);
                indices.Add(document.RootElement.GetProperty("Index").GetInt32());
            }

            Assert.Equal(200, indices.Count);
            Assert.Equal(Enumerable.Range(0, 200), indices.OrderBy(value => value));
        }

        [Fact]
        public void Constructor_DoesNotCreateLogFile()
        {
            using var temp = new TempDirectory();
            var logPath = Path.Combine(temp.Path, "a", "b", "zibra.log");

            using (var logger = new JsonLogger(logPath, LogLevel.Debug))
            {
                // No events are written.
            }

            Assert.False(File.Exists(logPath));
        }

        [Fact]
        public void Write_CreatesMissingDirectoriesAndFile()
        {
            using var temp = new TempDirectory();
            var logPath = Path.Combine(temp.Path, "a", "b", "zibra.log");

            using (var logger = new JsonLogger(logPath, LogLevel.Debug))
            {
                logger.Debug("created");
            }

            Assert.True(File.Exists(logPath));
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            using var temp = new TempDirectory();
            var logPath = Path.Combine(temp.Path, "zibra.log");
            var logger = new JsonLogger(logPath, LogLevel.Debug);

            logger.Info("first");
            logger.Dispose();
            logger.Dispose();

            Assert.Single(File.ReadAllLines(logPath));
        }
    }
}
