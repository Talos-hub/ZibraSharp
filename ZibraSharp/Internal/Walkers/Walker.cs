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

        /// <summary>
        /// Options for scanning a dir.
        /// </summary>
        private readonly EnumerationOptions _options;

        public Walker(string pathToDir)
        {
            _pathToDir = pathToDir ?? throw new ArgumentNullException(nameof(pathToDir));

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
        /// This method scans the directory.
        /// </summary>
        /// <returns>A collection containing file paths.</returns>
        public Task<IReadOnlyList<string>> WalkAsync(CancellationToken ct = default)
        {
            if (!Directory.Exists(_pathToDir))
            {
                throw new AppException(
                    AppErrorCode.Open,
                    nameof(WalkAsync),
                    $"Directory does not exist: {_pathToDir}");
            }

            var paths = new List<string>();
            foreach (var file in Directory.EnumerateFiles(_pathToDir, "*", _options))
            {
                ct.ThrowIfCancellationRequested();
                paths.Add(file);
            }

            return Task.FromResult<IReadOnlyList<string>>(paths);
        }
    }
}
