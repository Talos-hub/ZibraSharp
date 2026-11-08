using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Exceptions;
using ZibraSharp.Internal.Ports;

namespace ZibraSharp.Internal.Walkers
{
    /// <summary>
    /// This class implements the IWalker interface — logic for scanning directories.
    /// </summary>
    public sealed class Walker : IWalker
    {
        /// <summary>
        /// It is the path to dir that we want to scan.
        /// </summary>
        private readonly string _pathToDir;

        private readonly IConfig _config;

        /// <summary>
        /// Options for scanning a dir.
        /// </summary>
        private readonly EnumerationOptions _options;

        public Walker(string pathToDir, IConfig config)
        {
            _pathToDir = pathToDir ?? throw new ArgumentNullException(nameof(pathToDir));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            // set up options
            _options = new EnumerationOptions
            {
                RecurseSubdirectories = true, // All Dirs
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System, // skip system files
                MatchCasing = MatchCasing.CaseInsensitive
            };
        }

        /// <summary>
        /// This method scans the directory and filters excluded extensions.
        /// </summary>
        /// <returns>A collection containing file paths.</returns>
        public async Task<IReadOnlyList<string>> WalkAsync(CancellationToken ct = default)
        {
            if (!Directory.Exists(_pathToDir))
            {
                throw new AppException(
                    AppErrorCode.Open,
                    nameof(WalkAsync),
                    $"Directory does not exist: {_pathToDir}");
            }

            var paths = new List<string>();
            try
            {
                foreach (var file in Directory.EnumerateFiles(_pathToDir, "*", _options))
                {
                    ct.ThrowIfCancellationRequested();
                    paths.Add(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new AppException(
                    AppErrorCode.Read,
                    nameof(WalkAsync),
                    $"Failed to scan directory: {_pathToDir}",
                    ex);
            }

            if (paths.Count == 0)
            {
                return paths;
            }

            var extensions = await _config.GetExtensionsAsync(ct).ConfigureAwait(false);
            return Match(paths, extensions);
        }

        /// <summary>
        /// Removes files whose names end with one of the excluded extensions.
        /// </summary>
        private static IReadOnlyList<string> Match(
            IReadOnlyList<string> paths,
            IReadOnlySet<string> extensions)
        {
            if (extensions.Count == 0)
            {
                return paths;
            }

            var result = new List<string>(paths.Count);
            foreach (var path in paths)
            {
                var fileName = Path.GetFileName(path);
                var excluded = false;

                foreach (var extension in extensions)
                {
                    if (string.IsNullOrEmpty(extension))
                    {
                        continue;
                    }

                    if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        excluded = true;
                        break;
                    }
                }

                if (!excluded)
                {
                    result.Add(path);
                }
            }

            return result;
        }
    }
}
