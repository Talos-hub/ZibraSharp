using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Api;
using ZibraSharp.Internal.Archivers;
using ZibraSharp.Internal.Config;
using ZibraSharp.Internal.Exceptions;
using ZibraSharp.Internal.Logger;
using ZibraSharp.Internal.Ports;
using ZibraSharp.Internal.Services;
using ZibraSharp.Internal.Walkers;

namespace ZibraSharp.CLI
{
    /// <summary>
    /// Command-line entry point. Dependencies are wired manually (constructor DI),
    /// without a DI container.
    /// </summary>
    public static class CliApp
    {
        private const int ExitSuccess = 0;
        private const int ExitError = 1;
        private const int ExitPending = 2;
        private const int ExitCancelled = 130;

        public static async Task<int> RunAsync(string[] args)
        {
            using var cts = new CancellationTokenSource();
            var cancelled = 0;

            ConsoleCancelEventHandler handler = (_, eventArgs) =>
            {
                // First Ctrl+C: cancel gracefully. Second: let the runtime terminate.
                if (Interlocked.Exchange(ref cancelled, 1) == 0)
                {
                    eventArgs.Cancel = true;
                    cts.Cancel();
                }
            };

            Console.CancelKeyPress += handler;
            try
            {
                return await RunAsync(
                    args,
                    Directory.GetCurrentDirectory(),
                    Console.In,
                    Console.Out,
                    Console.Error,
                    interactive: !Console.IsInputRedirected,
                    cts.Token).ConfigureAwait(false);
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }
        }

        /// <summary>
        /// Testable overload: all console and filesystem roots are injected.
        /// </summary>
        internal static async Task<int> RunAsync(
            string[] args,
            string baseDirectory,
            TextReader input,
            TextWriter output,
            TextWriter error,
            bool interactive,
            CancellationToken ct = default)
        {
            CliOptions options;
            try
            {
                options = CliOptions.Parse(args);
            }
            catch (AppException ex)
            {
                error.WriteLine($"Error: {ex.Message}");
                return ExitError;
            }

            if (options.ShowVersion)
            {
                PrintVersion(output);
                return ExitSuccess;
            }

            if (options.Help || args.Length == 0)
            {
                PrintUsage(output);
                return args.Length == 0 ? ExitError : ExitSuccess;
            }

            var config = new JsonConfig(baseDirectory);
            var pendingStore = new JsonPendingUploadStore(Path.Combine(baseDirectory, "pending.json"));

            using var logger = new JsonLogger(
                Path.Combine(baseDirectory, JsonLogger.DefaultFileName),
                options.Verbose ? LogLevel.Debug : LogLevel.Info);

            var api = new LazyGoogleDriveApi(token =>
                GoogleDriveApiFactory.CreateAsync(
                    credentialsPath: null,
                    tokenPath: Path.Combine(baseDirectory, "token.json"),
                    ct: token));

            var pendingUploader = new PendingUploader(api, logger, pendingStore, options.KeepArchive);

            try
            {
                return options.Command switch
                {
                    "start" => await StartAsync(
                        options, config, logger, api, pendingStore, pendingUploader,
                        input, output, error, interactive, ct).ConfigureAwait(false),
                    "settings" => await SettingsAsync(options, config, input, output, error, ct)
                        .ConfigureAwait(false),
                    "extensions" => await ExtensionsAsync(options, config, input, output, ct)
                        .ConfigureAwait(false),
                    "log" => ShowLog(options, baseDirectory, output),
                    "pending" => await PendingAsync(options, pendingUploader, output, ct)
                        .ConfigureAwait(false),
                    "version" => PrintVersionAndReturnSuccess(output),
                    _ => UnknownCommand(options.Command, error, output)
                };
            }
            catch (OperationCanceledException)
            {
                logger.Warn("Operation cancelled");
                error.WriteLine("Operation cancelled.");
                return ExitCancelled;
            }
            catch (AppException ex)
            {
                logger.Error(ex, "Command failed", "Command", options.Command);
                error.WriteLine($"Error: {ex.Message}");
                return ExitError;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Unexpected error");
                error.WriteLine($"Unexpected error: {ex.Message}");
                return ExitError;
            }
        }

        private static async Task<int> StartAsync(
            CliOptions options,
            JsonConfig config,
            ILogger logger,
            IApiCloud api,
            IPendingUploadStore pendingStore,
            PendingUploader pendingUploader,
            TextReader input,
            TextWriter output,
            TextWriter error,
            bool interactive,
            CancellationToken ct)
        {
            if (options.Positionals.Count == 0 || options.Positionals.Count > 2)
            {
                error.WriteLine("Usage: zibrasharp start [archive.zip] <directory> [options]");
                return ExitError;
            }

            string archiveName;
            string directoryValue;
            if (options.Positionals.Count == 1)
            {
                directoryValue = options.Positionals[0];
                archiveName = BuildDefaultArchiveName(directoryValue);
            }
            else
            {
                archiveName = options.Positionals[0];
                directoryValue = options.Positionals[1];
            }

            if (!archiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                archiveName += ".zip";
            }

            var directory = Path.GetFullPath(directoryValue);
            if (!Directory.Exists(directory))
            {
                error.WriteLine($"Directory does not exist: {directory}");
                return ExitError;
            }

            if (options.DryRun)
            {
                var walker = new Walker(directory, config);
                var paths = await walker.WalkAsync(ct).ConfigureAwait(false);
                var totalBytes = GetTotalSize(paths);
                output.WriteLine($"Dry run: {paths.Count} file(s), {FormatSize(totalBytes)} would be archived.");
                output.WriteLine($"Source: {directory}");
                output.WriteLine($"Archive name: {archiveName}");
                return ExitSuccess;
            }

            var zipDirConfig = await config.GetDirAsync(ct).ConfigureAwait(false);
            if (zipDirConfig is null || string.IsNullOrWhiteSpace(zipDirConfig.ZipDir))
            {
                error.WriteLine("Backup directory is not configured. Run `zibrasharp settings` first.");
                return ExitError;
            }

            var archivePath = Path.Combine(zipDirConfig.ZipDir, archiveName);
            if (File.Exists(archivePath) && !options.Force)
            {
                error.WriteLine($"Archive already exists: {archivePath}");
                error.WriteLine("Use --force to replace it.");
                return ExitError;
            }

            if (GoogleCredentialsLocator.Find() is null)
            {
                error.WriteLine(
                    "credentials.json not found. Place it next to the executable, " +
                    $"set {GoogleCredentialsLocator.EnvironmentVariable}, or use ~/.ZibraSharp/credentials.json.");
                return ExitError;
            }

            await OfferPendingUploadAsync(pendingUploader, options.Yes, input, output, interactive, ct)
                .ConfigureAwait(false);

            var archiver = new ZipArchiver(
                zipDirConfig.ZipDir,
                directory,
                archiveName,
                maxWorkers: options.Workers,
                overwrite: options.Force);
            var service = new Service(
                new Walker(directory, config),
                archiver,
                api,
                logger,
                pendingStore,
                options.KeepArchive);

            output.WriteLine($"Scanning: {directory}");
            output.WriteLine($"Archive:  {archivePath}");

            var progress = new TextWriterProgress(output, interactive);
            var result = await service.RunAsync(ct, progress).ConfigureAwait(false);

            if (result.Status == ServiceStatus.Completed)
            {
                output.WriteLine("Successful! Archive uploaded to Google Drive.");
                if (options.KeepArchive)
                {
                    output.WriteLine($"Archive kept at: {result.ArchivePath}");
                }

                return ExitSuccess;
            }

            output.WriteLine($"Upload failed: {result.Error?.Message}");
            output.WriteLine($"Archive kept at: {result.ArchivePath}");
            output.WriteLine("It will be offered for upload on the next run.");
            return ExitPending;
        }

        private static async Task<int> SettingsAsync(
            CliOptions options,
            JsonConfig config,
            TextReader input,
            TextWriter output,
            TextWriter error,
            CancellationToken ct)
        {
            if (options.Show)
            {
                var current = await config.GetDirAsync(ct).ConfigureAwait(false);
                output.WriteLine(
                    current is null || string.IsNullOrWhiteSpace(current.ZipDir)
                        ? "Backup directory is not configured."
                        : $"Backup directory: {current.ZipDir}");

                var extensions = await config.GetExtensionsAsync(ct).ConfigureAwait(false);
                output.WriteLine(extensions.Count == 0
                    ? "Excluded extensions: none"
                    : "Excluded extensions: " + string.Join(" ", extensions.OrderBy(item => item)));
                return ExitSuccess;
            }

            string? value;
            if (options.Positionals.Count > 0)
            {
                value = options.Positionals[0];
            }
            else
            {
                output.Write("Enter path for zip files: ");
                value = input.ReadLine();
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                error.WriteLine("Path is empty.");
                return ExitError;
            }

            var path = Path.GetFullPath(value.Trim());
            if (!Directory.Exists(path))
            {
                error.WriteLine($"Directory does not exist: {path}");
                return ExitError;
            }

            await config.SetZipDirAsync(path, ct).ConfigureAwait(false);
            output.WriteLine($"Backup directory saved: {path}");
            return ExitSuccess;
        }

        private static async Task<int> ExtensionsAsync(
            CliOptions options,
            JsonConfig config,
            TextReader input,
            TextWriter output,
            CancellationToken ct)
        {
            if (options.ListExtensions)
            {
                var extensions = await config.GetExtensionsAsync(ct).ConfigureAwait(false);
                if (extensions.Count == 0)
                {
                    output.WriteLine("No excluded extensions.");
                }
                else
                {
                    foreach (var extension in extensions.OrderBy(item => item))
                    {
                        output.WriteLine(extension);
                    }
                }

                return ExitSuccess;
            }

            if (options.Remove)
            {
                await config.RemoveExtensionsFileAsync(ct).ConfigureAwait(false);
                output.WriteLine("Extensions file removed.");
                return ExitSuccess;
            }

            string inputValue;
            if (options.Positionals.Count > 0)
            {
                inputValue = string.Join(" ", options.Positionals);
            }
            else
            {
                output.WriteLine("Please enter extensions like: .exe .txt .json");
                output.Write("> ");
                inputValue = input.ReadLine() ?? string.Empty;
            }

            var saved = await config.CreateExtensionsAsync(inputValue, ct).ConfigureAwait(false);
            output.WriteLine("Saved extensions: " + string.Join(", ", saved.OrderBy(item => item)));
            return ExitSuccess;
        }

        private static int ShowLog(CliOptions options, string baseDirectory, TextWriter output)
        {
            var logPath = Path.Combine(baseDirectory, JsonLogger.DefaultFileName);
            if (!File.Exists(logPath))
            {
                output.WriteLine("Log file does not exist yet.");
                return ExitSuccess;
            }

            // The logger keeps the file open for writing; allow reading concurrently.
            using var stream = new FileStream(
                logPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            foreach (var line in ReadLines(reader).TakeLast(options.Lines))
            {
                output.WriteLine(line);
            }

            return ExitSuccess;
        }

        private static IEnumerable<string> ReadLines(StreamReader reader)
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                yield return line;
            }
        }

        private static async Task<int> PendingAsync(
            CliOptions options,
            PendingUploader uploader,
            TextWriter output,
            CancellationToken ct)
        {
            if (options.Positionals.Count > 0
                && options.Positionals[0].Equals("upload", StringComparison.OrdinalIgnoreCase))
            {
                var pending = await uploader.GetPendingAsync(ct).ConfigureAwait(false);
                if (pending.Count == 0)
                {
                    output.WriteLine("No pending archives.");
                    return ExitSuccess;
                }

                output.WriteLine($"Uploading {pending.Count} archive(s)...");
                var results = await uploader.UploadPendingAsync(ct).ConfigureAwait(false);
                PrintUploadSummary(results, output);
                return ExitSuccess;
            }

            var paths = await uploader.GetPendingAsync(ct).ConfigureAwait(false);
            if (paths.Count == 0)
            {
                output.WriteLine("No pending archives.");
                return ExitSuccess;
            }

            output.WriteLine("Pending archives:");
            foreach (var path in paths)
            {
                output.WriteLine($"  {path}");
            }

            output.WriteLine("Run `zibrasharp pending upload` to upload them.");
            return ExitSuccess;
        }

        private static async Task OfferPendingUploadAsync(
            PendingUploader uploader,
            bool autoConfirm,
            TextReader input,
            TextWriter output,
            bool interactive,
            CancellationToken ct)
        {
            var pending = await uploader.GetPendingAsync(ct).ConfigureAwait(false);
            if (pending.Count == 0)
            {
                return;
            }

            output.WriteLine($"Found {pending.Count} archive(s) waiting for upload:");
            foreach (var path in pending)
            {
                output.WriteLine($"  {path}");
            }

            if (!autoConfirm)
            {
                if (!interactive)
                {
                    output.WriteLine("Run `zibrasharp pending upload` to upload them.");
                    return;
                }

                output.Write("Upload them now? [y/N]: ");
                var answer = input.ReadLine();
                if (!IsYes(answer))
                {
                    return;
                }
            }

            output.WriteLine("Uploading pending archives...");
            var results = await uploader.UploadPendingAsync(ct).ConfigureAwait(false);
            PrintUploadSummary(results, output);
        }

        private static void PrintUploadSummary(IReadOnlyList<ServiceResult> results, TextWriter output)
        {
            var uploaded = results.Count(result => result.Status == ServiceStatus.Completed);
            var kept = results.Count(result => result.Status == ServiceStatus.PendingUpload);
            output.WriteLine($"Uploaded: {uploaded}, kept pending: {kept}");
        }

        private static string BuildDefaultArchiveName(string directory)
        {
            var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            var name = Path.GetFileName(trimmed);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "backup";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            return $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        }

        private static long GetTotalSize(IReadOnlyList<string> paths)
        {
            long total = 0;
            foreach (var path in paths)
            {
                try
                {
                    total += new FileInfo(path).Length;
                }
                catch (IOException)
                {
                    // File disappeared between scan and size calculation.
                }
            }

            return total;
        }

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            var unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0
                ? $"{bytes} {units[unit]}"
                : $"{value:0.##} {units[unit]}";
        }

        private static bool IsYes(string? answer)
        {
            return answer is not null
                && (answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
                    || answer.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));
        }

        private static void PrintVersion(TextWriter output)
        {
            var version = typeof(CliApp).Assembly.GetName().Version;
            output.WriteLine($"zibrasharp {version?.ToString(3) ?? "1.0.0"}");
        }

        private static int PrintVersionAndReturnSuccess(TextWriter output)
        {
            PrintVersion(output);
            return ExitSuccess;
        }

        private static int UnknownCommand(string command, TextWriter error, TextWriter output)
        {
            error.WriteLine($"Unknown command: {command}");
            PrintUsage(output);
            return ExitError;
        }

        private static void PrintUsage(TextWriter output)
        {
            output.WriteLine("ZibraSharp — backup directories to Google Drive");
            output.WriteLine();
            output.WriteLine("Usage:");
            output.WriteLine("  zibrasharp start [archive.zip] <directory> [options]");
            output.WriteLine("  zibrasharp settings [<path>] [--show]");
            output.WriteLine("  zibrasharp extensions [<extensions...>] [--list] [--remove]");
            output.WriteLine("  zibrasharp log [--lines N]");
            output.WriteLine("  zibrasharp pending [upload]");
            output.WriteLine("  zibrasharp version | --version");
            output.WriteLine("  zibrasharp help");
            output.WriteLine();
            output.WriteLine("Options for start:");
            output.WriteLine("  --yes, -y          Upload pending archives without asking");
            output.WriteLine("  --dry-run          Scan and report without creating an archive");
            output.WriteLine("  --force, -f        Replace an existing archive");
            output.WriteLine("  --keep-archive     Keep the archive after a successful upload");
            output.WriteLine("  --workers N        Number of compression workers (default: CPU count)");
            output.WriteLine();
            output.WriteLine("Global options:");
            output.WriteLine("  --verbose          Enable debug logging");
        }

        private sealed class TextWriterProgress : IProgress<ArchiveProgress>
        {
            private readonly TextWriter _output;
            private readonly bool _interactive;
            private readonly object _sync = new();
            private int _lastPercent = -1;

            public TextWriterProgress(TextWriter output, bool interactive)
            {
                _output = output;
                _interactive = interactive;
            }

            public void Report(ArchiveProgress value)
            {
                if (value.Total <= 0)
                {
                    return;
                }

                var percent = (int)Math.Min(100, value.Processed * 100L / value.Total);

                lock (_sync)
                {
                    if (percent == _lastPercent && value.Processed < value.Total)
                    {
                        return;
                    }

                    _lastPercent = percent;

                    if (_interactive)
                    {
                        _output.Write($"\rArchiving: {value.Processed}/{value.Total} ({percent}%)");
                        if (value.Processed >= value.Total)
                        {
                            _output.WriteLine();
                        }
                    }
                    else
                    {
                        _output.WriteLine($"Archiving: {value.Processed}/{value.Total} ({percent}%)");
                    }
                }
            }
        }

        /// <summary>
        /// Parsed command-line arguments. Options can appear before or after positionals.
        /// </summary>
        internal sealed class CliOptions
        {
            public string Command { get; private set; } = string.Empty;
            public List<string> Positionals { get; } = new();
            public bool Verbose { get; private set; }
            public bool DryRun { get; private set; }
            public bool Force { get; private set; }
            public bool KeepArchive { get; private set; }
            public bool Yes { get; private set; }
            public bool Show { get; private set; }
            public bool ListExtensions { get; private set; }
            public bool Remove { get; private set; }
            public bool Help { get; private set; }
            public bool ShowVersion { get; private set; }
            public int Workers { get; private set; }
            public int Lines { get; private set; } = 50;

            public static CliOptions Parse(string[] args)
            {
                var options = new CliOptions();
                if (args.Length == 0)
                {
                    return options;
                }

                options.Command = args[0].ToLowerInvariant();

                if (options.Command is "--version" or "version")
                {
                    options.ShowVersion = true;
                }
                else if (options.Command is "--help" or "-h" or "help")
                {
                    options.Help = true;
                }

                for (var index = 1; index < args.Length; index++)
                {
                    var arg = args[index];
                    switch (arg.ToLowerInvariant())
                    {
                        case "--verbose":
                            options.Verbose = true;
                            break;
                        case "--dry-run":
                            options.DryRun = true;
                            break;
                        case "--force":
                        case "-f":
                            options.Force = true;
                            break;
                        case "--keep-archive":
                            options.KeepArchive = true;
                            break;
                        case "--yes":
                        case "-y":
                            options.Yes = true;
                            break;
                        case "--show":
                            options.Show = true;
                            break;
                        case "--list":
                            options.ListExtensions = true;
                            break;
                        case "--remove":
                        case "-r":
                            options.Remove = true;
                            break;
                        case "--help":
                        case "-h":
                            options.Help = true;
                            break;
                        case "--version":
                            options.ShowVersion = true;
                            break;
                        case "--workers":
                            options.Workers = ReadInt(args, ref index, "--workers");
                            break;
                        case "--lines":
                        case "-n":
                            options.Lines = ReadPositiveInt(args, ref index, "--lines");
                            break;
                        default:
                            if (arg.StartsWith("--workers=", StringComparison.OrdinalIgnoreCase))
                            {
                                options.Workers = ParseInt(arg["--workers=".Length..], "--workers");
                            }
                            else if (arg.StartsWith("--lines=", StringComparison.OrdinalIgnoreCase))
                            {
                                options.Lines = ParsePositiveInt(arg["--lines=".Length..], "--lines");
                            }
                            else if (arg.StartsWith("-", StringComparison.Ordinal))
                            {
                                throw new AppException(
                                    AppErrorCode.Config,
                                    nameof(Parse),
                                    $"Unknown option: {arg}");
                            }
                            else
                            {
                                options.Positionals.Add(arg);
                            }

                            break;
                    }
                }

                return options;
            }

            private static int ReadInt(string[] args, ref int index, string name)
            {
                if (index + 1 >= args.Length)
                {
                    throw new AppException(
                        AppErrorCode.Config,
                        nameof(ReadInt),
                        $"Option {name} requires a value.");
                }

                index++;
                return ParseInt(args[index], name);
            }

            private static int ReadPositiveInt(string[] args, ref int index, string name)
            {
                if (index + 1 >= args.Length)
                {
                    throw new AppException(
                        AppErrorCode.Config,
                        nameof(ReadPositiveInt),
                        $"Option {name} requires a value.");
                }

                index++;
                return ParsePositiveInt(args[index], name);
            }

            private static int ParseInt(string value, string name)
            {
                if (!int.TryParse(value, out var parsed) || parsed < 0)
                {
                    throw new AppException(
                        AppErrorCode.Config,
                        nameof(ParseInt),
                        $"Option {name} expects a non-negative integer, got: {value}");
                }

                return parsed;
            }

            private static int ParsePositiveInt(string value, string name)
            {
                if (!int.TryParse(value, out var parsed) || parsed <= 0)
                {
                    throw new AppException(
                        AppErrorCode.Config,
                        nameof(ParsePositiveInt),
                        $"Option {name} expects a positive integer, got: {value}");
                }

                return parsed;
            }
        }
    }
}
