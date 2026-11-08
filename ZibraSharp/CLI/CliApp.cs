using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public static Task<int> RunAsync(string[] args)
        {
            return RunAsync(
                args,
                Directory.GetCurrentDirectory(),
                Console.In,
                Console.Out,
                Console.Error,
                interactive: !Console.IsInputRedirected);
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
            bool interactive)
        {
            var config = new JsonConfig(baseDirectory);
            var pendingStore = new JsonPendingUploadStore(Path.Combine(baseDirectory, "pending.json"));

            using var logger = new JsonLogger(
                Path.Combine(baseDirectory, JsonLogger.DefaultFileName),
                LogLevel.Info);

            var api = new LazyGoogleDriveApi(ct =>
                GoogleDriveApiFactory.CreateAsync(
                    credentialsPath: null,
                    tokenPath: Path.Combine(baseDirectory, "token.json"),
                    ct: ct));

            var pendingUploader = new PendingUploader(api, logger, pendingStore);

            try
            {
                if (args.Length == 0)
                {
                    PrintUsage(output);
                    return ExitError;
                }

                var command = args[0].ToLowerInvariant();
                var rest = args.Skip(1).ToArray();

                return command switch
                {
                    "start" => await StartAsync(
                        rest, config, logger, api, pendingStore, pendingUploader,
                        input, output, error, interactive),
                    "settings" => await SettingsAsync(config, input, output, error),
                    "extensions" => await ExtensionsAsync(rest, config, input, output),
                    "log" => ShowLog(rest, baseDirectory, output),
                    "pending" => await PendingAsync(rest, pendingUploader, output),
                    "help" or "--help" or "-h" => PrintUsageAndReturnSuccess(output),
                    _ => UnknownCommand(command, error, output)
                };
            }
            catch (OperationCanceledException)
            {
                error.WriteLine("Operation cancelled.");
                return ExitError;
            }
            catch (AppException ex)
            {
                logger.Error(ex, "Command failed", "Command", args.Length > 0 ? args[0] : "unknown");
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
            string[] args,
            JsonConfig config,
            ILogger logger,
            IApiCloud api,
            IPendingUploadStore pendingStore,
            PendingUploader pendingUploader,
            TextReader input,
            TextWriter output,
            TextWriter error,
            bool interactive)
        {
            if (args.Length < 2)
            {
                error.WriteLine("Usage: zibrasharp start <archive.zip> <directory> [--yes]");
                return ExitError;
            }

            var archiveName = args[0];
            if (!archiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                archiveName += ".zip";
            }

            var directory = Path.GetFullPath(args[1]);
            var autoConfirm = args.Skip(2).Any(arg =>
                arg.Equals("--yes", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("-y", StringComparison.OrdinalIgnoreCase));

            if (!Directory.Exists(directory))
            {
                error.WriteLine($"Directory does not exist: {directory}");
                return ExitError;
            }

            var zipDirConfig = await config.GetDirAsync();
            if (zipDirConfig is null || string.IsNullOrWhiteSpace(zipDirConfig.ZipDir))
            {
                error.WriteLine("Backup directory is not configured. Run `zibrasharp settings` first.");
                return ExitError;
            }

            if (GoogleCredentialsLocator.Find() is null)
            {
                error.WriteLine(
                    "credentials.json not found. Place it next to the executable, " +
                    $"set {GoogleCredentialsLocator.EnvironmentVariable}, or use ~/.ZibraSharp/credentials.json.");
                return ExitError;
            }

            await OfferPendingUploadAsync(pendingUploader, autoConfirm, input, output, interactive)
                .ConfigureAwait(false);

            var walker = new Walker(directory, config);
            var archiver = new ZipArchiver(zipDirConfig.ZipDir, directory, archiveName, maxWorkers: 0);
            var service = new Service(walker, archiver, api, logger, pendingStore);

            output.WriteLine($"Scanning: {directory}");
            output.WriteLine($"Archive:  {Path.Combine(zipDirConfig.ZipDir, archiveName)}");

            var result = await service.RunAsync().ConfigureAwait(false);

            if (result.Status == ServiceStatus.Completed)
            {
                output.WriteLine("Successful! Archive uploaded to Google Drive.");
                return ExitSuccess;
            }

            output.WriteLine($"Upload failed: {result.Error?.Message}");
            output.WriteLine($"Archive kept at: {result.ArchivePath}");
            output.WriteLine("It will be offered for upload on the next run.");
            return ExitPending;
        }

        private static async Task OfferPendingUploadAsync(
            PendingUploader uploader,
            bool autoConfirm,
            TextReader input,
            TextWriter output,
            bool interactive)
        {
            var pending = await uploader.GetPendingAsync().ConfigureAwait(false);
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
            var results = await uploader.UploadPendingAsync().ConfigureAwait(false);
            PrintUploadSummary(results, output);
        }

        private static async Task<int> SettingsAsync(
            JsonConfig config,
            TextReader input,
            TextWriter output,
            TextWriter error)
        {
            output.Write("Enter path for zip files: ");
            var value = input.ReadLine();
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

            await config.SetZipDirAsync(path).ConfigureAwait(false);
            output.WriteLine($"Backup directory saved: {path}");
            return ExitSuccess;
        }

        private static async Task<int> ExtensionsAsync(
            string[] args,
            JsonConfig config,
            TextReader input,
            TextWriter output)
        {
            if (args.Any(arg =>
                    arg.Equals("--remove", StringComparison.OrdinalIgnoreCase)
                    || arg.Equals("-r", StringComparison.OrdinalIgnoreCase)))
            {
                await config.RemoveExtensionsFileAsync().ConfigureAwait(false);
                output.WriteLine("Extensions file removed.");
                return ExitSuccess;
            }

            output.WriteLine("Please enter extensions like: .exe .txt .json");
            output.Write("> ");
            var value = input.ReadLine();

            var extensions = await config.CreateExtensionsAsync(value ?? string.Empty).ConfigureAwait(false);
            output.WriteLine("Saved extensions: " + string.Join(", ", extensions.OrderBy(item => item)));
            return ExitSuccess;
        }

        private static int ShowLog(string[] args, string baseDirectory, TextWriter output)
        {
            var lineCount = 50;
            for (var i = 0; i < args.Length; i++)
            {
                if ((args[i].Equals("--lines", StringComparison.OrdinalIgnoreCase)
                     || args[i].Equals("-n", StringComparison.OrdinalIgnoreCase))
                    && i + 1 < args.Length
                    && int.TryParse(args[i + 1], out var parsed)
                    && parsed > 0)
                {
                    lineCount = parsed;
                    i++;
                }
            }

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

            foreach (var line in ReadLines(reader).TakeLast(lineCount))
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
            string[] args,
            PendingUploader uploader,
            TextWriter output)
        {
            if (args.Length > 0 && args[0].Equals("upload", StringComparison.OrdinalIgnoreCase))
            {
                var pending = await uploader.GetPendingAsync().ConfigureAwait(false);
                if (pending.Count == 0)
                {
                    output.WriteLine("No pending archives.");
                    return ExitSuccess;
                }

                output.WriteLine($"Uploading {pending.Count} archive(s)...");
                var results = await uploader.UploadPendingAsync().ConfigureAwait(false);
                PrintUploadSummary(results, output);
                return ExitSuccess;
            }

            var paths = await uploader.GetPendingAsync().ConfigureAwait(false);
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

        private static void PrintUploadSummary(IReadOnlyList<ServiceResult> results, TextWriter output)
        {
            var uploaded = results.Count(result => result.Status == ServiceStatus.Completed);
            var kept = results.Count(result => result.Status == ServiceStatus.PendingUpload);
            output.WriteLine($"Uploaded: {uploaded}, kept pending: {kept}");
        }

        private static bool IsYes(string? answer)
        {
            return answer is not null
                && (answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
                    || answer.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));
        }

        private static int PrintUsageAndReturnSuccess(TextWriter output)
        {
            PrintUsage(output);
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
            output.WriteLine("  zibrasharp start <archive.zip> <directory> [--yes]");
            output.WriteLine("  zibrasharp settings");
            output.WriteLine("  zibrasharp extensions [--remove]");
            output.WriteLine("  zibrasharp log [--lines N]");
            output.WriteLine("  zibrasharp pending [upload]");
            output.WriteLine("  zibrasharp help");
        }
    }
}
