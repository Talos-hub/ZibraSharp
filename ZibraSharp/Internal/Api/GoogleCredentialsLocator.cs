using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Exceptions;

namespace ZibraSharp.Internal.Api
{
    /// <summary>
    /// Finds and validates Google OAuth credentials.
    /// Search order: explicit path, environment variable, current directory,
    /// then the user profile directory.
    /// </summary>
    public static class GoogleCredentialsLocator
    {
        public const string EnvironmentVariable = "CREDENTIALS_ZIBRA";
        public const string AlternativeEnvironmentVariable = "CREDENTIALS_ZIBRASHARP";

        public static IReadOnlyList<string> GetCandidates(string? explicitPath = null)
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                candidates.Add(explicitPath);
            }

            AddEnvironmentCandidate(candidates, EnvironmentVariable);
            AddEnvironmentCandidate(candidates, AlternativeEnvironmentVariable);

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var appDirectory = Path.Combine(home, ".ZibraSharp");

            candidates.Add("credentials.json");
            candidates.Add(Path.Combine("config", "credentials.json"));
            candidates.Add(Path.Combine("configuration", "credentials.json"));
            candidates.Add(Path.Combine("settings", "credentials.json"));
            candidates.Add(Path.Combine(appDirectory, "credentials.json"));
            candidates.Add(Path.Combine(appDirectory, "config", "credentials.json"));
            candidates.Add(Path.Combine(appDirectory, "configuration", "credentials.json"));
            candidates.Add(Path.Combine(appDirectory, "settings", "credentials.json"));

            return candidates;
        }

        public static string? Find(string? explicitPath = null)
        {
            return GetCandidates(explicitPath).FirstOrDefault(File.Exists);
        }

        public static async Task<byte[]> LoadAsync(string? explicitPath = null, CancellationToken ct = default)
        {
            var path = Find(explicitPath);
            if (path is null)
            {
                throw new AppException(
                    AppErrorCode.Config,
                    nameof(LoadAsync),
                    "credentials.json not found. Place it next to the executable, " +
                    $"set {EnvironmentVariable}, or use ~/.ZibraSharp/credentials.json.");
            }

            byte[] content;
            try
            {
                content = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new AppException(
                    AppErrorCode.Open,
                    nameof(LoadAsync),
                    $"Failed to read credentials file: {path}",
                    ex);
            }

            try
            {
                using var _ = JsonDocument.Parse(content);
            }
            catch (JsonException ex)
            {
                throw new AppException(
                    AppErrorCode.Parse,
                    nameof(LoadAsync),
                    $"Credentials file is not valid JSON: {path}",
                    ex);
            }

            return content;
        }

        private static void AddEnvironmentCandidate(List<string> candidates, string variable)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                candidates.Add(value);
            }
        }
    }
}
