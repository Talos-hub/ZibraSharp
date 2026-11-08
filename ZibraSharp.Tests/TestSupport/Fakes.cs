using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Ports;

namespace ZibraSharp.Tests.TestSupport
{
    internal sealed class FakeLogger : ILogger
    {
        private readonly object _sync = new();
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (_sync)
                {
                    return _entries.ToArray();
                }
            }
        }

        public void Info(string message, params object?[] args) => Add("INFO", message, args, null);

        public void Warn(string message, params object?[] args) => Add("WARN", message, args, null);

        public void Debug(string message, params object?[] args) => Add("DEBUG", message, args, null);

        public void Error(string message, params object?[] args) => Add("ERROR", message, args, null);

        public void Error(Exception exception, string message, params object?[] args) =>
            Add("ERROR", message, args, exception);

        private void Add(string level, string message, object?[] args, Exception? exception)
        {
            lock (_sync)
            {
                _entries.Add(new LogEntry(level, message, args, exception));
            }
        }

        internal sealed record LogEntry(string Level, string Message, object?[] Args, Exception? Exception);
    }

    internal sealed class FakeWalker : IWalker
    {
        public IReadOnlyList<string> Paths { get; set; } = Array.Empty<string>();
        public Exception? Exception { get; set; }
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> WalkAsync(CancellationToken ct = default)
        {
            Calls++;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Paths);
        }
    }

    internal sealed class FakeArchiver : IArchiver
    {
        public string ArchivePath { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
        public IReadOnlyList<string>? ReceivedPaths { get; private set; }
        public ArchiveProgress? LastProgress { get; private set; }
        public int Calls { get; private set; }

        public Task<string> StartAsync(
            IReadOnlyList<string> paths,
            CancellationToken ct = default,
            IProgress<ArchiveProgress>? progress = null)
        {
            Calls++;
            ReceivedPaths = paths;

            if (Exception is not null)
            {
                throw Exception;
            }

            var directory = System.IO.Path.GetDirectoryName(ArchivePath);
            if (!string.IsNullOrEmpty(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.IO.File.WriteAllText(ArchivePath, "fake archive");

            if (progress is not null && paths.Count > 0)
            {
                var last = new ArchiveProgress(paths.Count, paths.Count, paths[^1]);
                LastProgress = last;
                progress.Report(last);
            }

            return Task.FromResult(ArchivePath);
        }
    }

    internal sealed class FakeApiCloud : IApiCloud
    {
        public Exception? CheckException { get; set; }
        public Exception? UploadException { get; set; }
        public Func<string, Exception?>? UploadExceptionFactory { get; set; }
        public int CheckCalls { get; private set; }
        public List<string> UploadedPaths { get; } = new();

        public Task CheckAsync(CancellationToken ct = default)
        {
            CheckCalls++;
            if (CheckException is not null)
            {
                throw CheckException;
            }

            return Task.CompletedTask;
        }

        public Task UploadFileAsync(string filePath, CancellationToken ct = default)
        {
            var exception = UploadExceptionFactory?.Invoke(filePath) ?? UploadException;
            if (exception is not null)
            {
                throw exception;
            }

            UploadedPaths.Add(filePath);
            return Task.CompletedTask;
        }
    }

    internal sealed class InMemoryPendingUploadStore : IPendingUploadStore
    {
        private readonly object _sync = new();
        private readonly List<string> _paths = new();

        public IReadOnlyList<string> Paths
        {
            get
            {
                lock (_sync)
                {
                    return _paths.ToArray();
                }
            }
        }

        public Task<IReadOnlyList<string>> GetAllAsync(CancellationToken ct = default)
        {
            lock (_sync)
            {
                return Task.FromResult<IReadOnlyList<string>>(_paths.ToArray());
            }
        }

        public Task AddAsync(string archivePath, CancellationToken ct = default)
        {
            lock (_sync)
            {
                if (!_paths.Contains(archivePath))
                {
                    _paths.Add(archivePath);
                }
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync(string archivePath, CancellationToken ct = default)
        {
            lock (_sync)
            {
                _paths.Remove(archivePath);
            }

            return Task.CompletedTask;
        }
    }
}
