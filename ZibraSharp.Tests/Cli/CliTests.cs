using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ZibraSharp.CLI;
using ZibraSharp.Tests.TestSupport;

namespace ZibraSharp.Tests.Cli
{
    public sealed class CliTests
    {
        [Fact]
        public async Task Help_PrintsUsageAndReturnsSuccess()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty, "help");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Usage:", result.Output);
            Assert.Contains("zibrasharp start", result.Output);
        }

        [Fact]
        public async Task NoArguments_PrintsUsageAndReturnsError()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Usage:", result.Output);
        }

        [Fact]
        public async Task UnknownCommand_ReturnsError()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty, "nope");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Unknown command: nope", result.Error);
        }

        [Fact]
        public async Task Settings_SavesZipDir()
        {
            using var temp = new TempDirectory();
            var backup = temp.CreateDirectory("backups");

            var result = await RunAsync(temp.Path, backup + "\n", "settings");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Backup directory saved", result.Output);

            var raw = await File.ReadAllTextAsync(Path.Combine(temp.Path, "zipdir.json"));
            using var document = JsonDocument.Parse(raw);
            Assert.Equal(backup, document.RootElement.GetProperty("zipdir").GetString());
        }

        [Fact]
        public async Task Settings_RejectsMissingDirectory()
        {
            using var temp = new TempDirectory();
            var missing = Path.Combine(temp.Path, "missing");

            var result = await RunAsync(temp.Path, missing + "\n", "settings");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Directory does not exist", result.Error);
        }

        [Fact]
        public async Task Settings_RejectsEmptyInput()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, "\n", "settings");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Path is empty", result.Error);
        }

        [Fact]
        public async Task Extensions_SavesExtensions()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, ".txt .exe\n", "extensions");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(".exe", result.Output);

            var raw = await File.ReadAllTextAsync(Path.Combine(temp.Path, "extensions.json"));
            using var document = JsonDocument.Parse(raw);
            Assert.True(document.RootElement.GetProperty(".txt").GetBoolean());
            Assert.True(document.RootElement.GetProperty(".exe").GetBoolean());
        }

        [Fact]
        public async Task Extensions_Remove_DeletesFile()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "extensions.json");
            await File.WriteAllTextAsync(path, "{}");

            var result = await RunAsync(temp.Path, string.Empty, "extensions", "--remove");

            Assert.Equal(0, result.ExitCode);
            Assert.False(File.Exists(path));
        }

        [Fact]
        public async Task Log_WhenMissing_PrintsMessage()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty, "log");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Log file does not exist yet", result.Output);
        }

        [Fact]
        public async Task Log_PrintsRequestedNumberOfLines()
        {
            using var temp = new TempDirectory();
            await File.WriteAllTextAsync(
                Path.Combine(temp.Path, "zibra.log"),
                "line1\nline2\nline3\n");

            var result = await RunAsync(temp.Path, string.Empty, "log", "--lines", "2");

            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotContain("line1", result.Output);
            Assert.Contains("line2", result.Output);
            Assert.Contains("line3", result.Output);
        }

        [Fact]
        public async Task Pending_WhenEmpty_PrintsNoPending()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty, "pending");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("No pending archives", result.Output);
        }

        [Fact]
        public async Task Pending_ListsArchives()
        {
            using var temp = new TempDirectory();
            var archive = Path.Combine(temp.Path, "archive.zip");
            await File.WriteAllTextAsync(
                Path.Combine(temp.Path, "pending.json"),
                JsonSerializer.Serialize(new[] { archive }));

            var result = await RunAsync(temp.Path, string.Empty, "pending");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(archive, result.Output);
        }

        [Fact]
        public async Task Start_WithoutArguments_ReturnsUsageError()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty, "start");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Usage: zibrasharp start", result.Error);
        }

        [Fact]
        public async Task Start_WithMissingDirectory_ReturnsError()
        {
            using var temp = new TempDirectory();
            var missing = Path.Combine(temp.Path, "missing");

            var result = await RunAsync(temp.Path, string.Empty, "start", "backup.zip", missing);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Directory does not exist", result.Error);
        }

        [Fact]
        public async Task Start_WithoutZipDirConfig_ReturnsError()
        {
            using var temp = new TempDirectory();
            var source = temp.CreateDirectory("source");

            var result = await RunAsync(temp.Path, string.Empty, "start", "backup.zip", source);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("not configured", result.Error);
        }

        [Fact]
        public async Task Version_PrintsAssemblyVersion()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty, "version");

            Assert.Equal(0, result.ExitCode);
            Assert.StartsWith("zibrasharp 1.0.0", result.Output);
        }

        [Fact]
        public async Task VersionFlag_PrintsAssemblyVersion()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty, "--version");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("zibrasharp 1.0.0", result.Output);
        }

        [Fact]
        public async Task SettingsShow_ReportsUnconfiguredState()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty, "settings", "--show");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Backup directory is not configured", result.Output);
            Assert.Contains("Excluded extensions: none", result.Output);
        }

        [Fact]
        public async Task SettingsShow_ReportsConfiguredValues()
        {
            using var temp = new TempDirectory();
            var backup = temp.CreateDirectory("backups");
            await File.WriteAllTextAsync(
                Path.Combine(temp.Path, "zipdir.json"),
                "{\"zipdir\":" + JsonSerializer.Serialize(backup) + "}");
            await File.WriteAllTextAsync(
                Path.Combine(temp.Path, "extensions.json"),
                "{\".txt\":true,\".exe\":true}");

            var result = await RunAsync(temp.Path, string.Empty, "settings", "--show");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(backup, result.Output);
            Assert.Contains(".txt", result.Output);
            Assert.Contains(".exe", result.Output);
        }

        [Fact]
        public async Task Settings_WithPositionalPath_SavesWithoutPrompting()
        {
            using var temp = new TempDirectory();
            var backup = temp.CreateDirectory("backups");

            var result = await RunAsync(temp.Path, string.Empty, "settings", backup);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Backup directory saved", result.Output);

            var raw = await File.ReadAllTextAsync(Path.Combine(temp.Path, "zipdir.json"));
            using var document = JsonDocument.Parse(raw);
            Assert.Equal(backup, document.RootElement.GetProperty("zipdir").GetString());
        }

        [Fact]
        public async Task ExtensionsList_WhenEmpty_PrintsMessage()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty, "extensions", "--list");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("No excluded extensions", result.Output);
        }

        [Fact]
        public async Task ExtensionsList_PrintsConfiguredExtensions()
        {
            using var temp = new TempDirectory();
            await File.WriteAllTextAsync(
                Path.Combine(temp.Path, "extensions.json"),
                "{\".txt\":true,\".exe\":true,\".bin\":false}");

            var result = await RunAsync(temp.Path, string.Empty, "extensions", "--list");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(".txt", result.Output);
            Assert.Contains(".exe", result.Output);
            Assert.DoesNotContain(".bin", result.Output);
        }

        [Fact]
        public async Task Extensions_WithPositionalValues_SavesWithoutPrompting()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty, "extensions", ".txt", ".exe");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains(".txt", result.Output);
            Assert.Contains(".exe", result.Output);

            var raw = await File.ReadAllTextAsync(Path.Combine(temp.Path, "extensions.json"));
            using var document = JsonDocument.Parse(raw);
            Assert.True(document.RootElement.GetProperty(".txt").GetBoolean());
            Assert.True(document.RootElement.GetProperty(".exe").GetBoolean());
        }

        [Fact]
        public async Task UnknownOption_ReturnsError()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty, "start", "--nope");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Unknown option: --nope", result.Error);
        }

        [Fact]
        public async Task WorkersOption_RejectsMissingValue()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty, "start", "dir", "--workers");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("requires a value", result.Error);
        }

        [Fact]
        public async Task WorkersOption_RejectsInvalidValue()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty, "start", "dir", "--workers", "many");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("expects a non-negative integer", result.Error);
        }

        [Fact]
        public async Task DryRun_ScansWithoutConfigurationOrCredentials()
        {
            using var temp = new TempDirectory();
            var source = temp.CreateDirectory("source");
            temp.CreateFile(Path.Combine("source", "a.txt"), "hello");
            temp.CreateFile(Path.Combine("source", "sub", "b.txt"), "world!");

            var result = await RunAsync(temp.Path, string.Empty, "start", source, "--dry-run");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Dry run: 2 file(s)", result.Output);
            Assert.Contains("would be archived", result.Output);
            Assert.Contains("Archive name: source-", result.Output);
        }

        [Fact]
        public async Task DryRun_WithExplicitArchiveName_UsesIt()
        {
            using var temp = new TempDirectory();
            var source = temp.CreateDirectory("source");
            temp.CreateFile(Path.Combine("source", "a.txt"), "hello");

            var result = await RunAsync(temp.Path, string.Empty, "start", "custom.zip", source, "--dry-run");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Archive name: custom.zip", result.Output);
        }

        [Fact]
        public async Task Start_WhenArchiveExists_WithoutForce_ReturnsHint()
        {
            using var temp = new TempDirectory();
            var source = temp.CreateDirectory("source");
            var backup = temp.CreateDirectory("backups");
            temp.CreateFile(Path.Combine("source", "a.txt"), "hello");
            temp.CreateFile(Path.Combine("backups", "backup.zip"), "existing");
            await File.WriteAllTextAsync(
                Path.Combine(temp.Path, "zipdir.json"),
                "{\"zipdir\":" + JsonSerializer.Serialize(backup) + "}");

            var result = await RunAsync(temp.Path, string.Empty, "start", "backup.zip", source);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Archive already exists", result.Error);
            Assert.Contains("--force", result.Error);
        }

        [Fact]
        public async Task Start_WithForce_ProceedsPastExistingArchive()
        {
            using var temp = new TempDirectory();
            var source = temp.CreateDirectory("source");
            var backup = temp.CreateDirectory("backups");
            temp.CreateFile(Path.Combine("source", "a.txt"), "hello");
            temp.CreateFile(Path.Combine("backups", "backup.zip"), "existing");
            await File.WriteAllTextAsync(
                Path.Combine(temp.Path, "zipdir.json"),
                "{\"zipdir\":" + JsonSerializer.Serialize(backup) + "}");

            var result = await RunAsync(temp.Path, string.Empty, "start", "backup.zip", source, "--force");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("credentials.json not found", result.Error);
            Assert.DoesNotContain("Archive already exists", result.Error);
        }

        [Fact]
        public async Task Start_WithTooManyPositionals_ReturnsUsageError()
        {
            using var temp = new TempDirectory();

            var result = await RunAsync(temp.Path, string.Empty, "start", "a.zip", "b", "c");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Usage: zibrasharp start", result.Error);
        }

        private static async Task<CliResult> RunAsync(
            string baseDirectory,
            string input,
            params string[] args)
        {
            using var inputReader = new StringReader(input);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CliApp.RunAsync(
                args,
                baseDirectory,
                inputReader,
                output,
                error,
                interactive: false);

            return new CliResult(exitCode, output.ToString(), error.ToString());
        }

        private sealed record CliResult(int ExitCode, string Output, string Error);
    }
}
