using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Exceptions;
using ZibraSharp.Internal.Ports;

namespace ZibraSharp.Internal.Archivers
{
    /// <summary>
    /// Creates ZIP archives. Files are compressed in parallel by a worker pool,
    /// then written into the archive by a single writer (ZIP central directory
    /// must be written sequentially).
    /// </summary>
    public sealed class ZipArchiver : IArchiver
    {
        private const int BufferSize = 81920;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly string _pathToFolder;
        private readonly string _basePath;
        private readonly string _nameZip;
        private readonly int _maxWorkers;
        private readonly bool _overwrite;

        /// <param name="pathToFolder">Directory where the archive will be created.</param>
        /// <param name="basePath">Root directory used to calculate relative paths inside the archive.</param>
        /// <param name="nameZip">Archive file name, for example "backup.zip".</param>
        /// <param name="maxWorkers">Number of compression workers. 0 or less means <see cref="Environment.ProcessorCount"/>.</param>
        /// <param name="overwrite">When true, an existing archive is replaced atomically after the new one is fully written.</param>
        public ZipArchiver(
            string pathToFolder,
            string basePath,
            string nameZip,
            int maxWorkers = 0,
            bool overwrite = false)
        {
            _pathToFolder = Path.GetFullPath(pathToFolder ?? throw new ArgumentNullException(nameof(pathToFolder)));
            _basePath = Path.GetFullPath(basePath ?? throw new ArgumentNullException(nameof(basePath)));

            if (string.IsNullOrWhiteSpace(nameZip))
            {
                throw new ArgumentException("Archive name must not be empty.", nameof(nameZip));
            }

            _nameZip = nameZip;
            _maxWorkers = maxWorkers > 0 ? maxWorkers : Math.Max(1, Environment.ProcessorCount);
            _overwrite = overwrite;
        }

        public async Task<string> StartAsync(
            IReadOnlyList<string> paths,
            CancellationToken ct = default,
            IProgress<ArchiveProgress>? progress = null)
        {
            if (paths is null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            if (paths.Count == 0)
            {
                throw new AppException(AppErrorCode.EmptyDirectory, nameof(StartAsync), "No files to archive.");
            }

            ValidateFolders();

            var archivePath = Path.Combine(_pathToFolder, _nameZip);
            if (File.Exists(archivePath) && !_overwrite)
            {
                throw new AppException(
                    AppErrorCode.Create,
                    nameof(StartAsync),
                    $"Archive already exists: {archivePath}");
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "ZibraSharp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            // Write next to the destination so the final move stays on the same
            // volume and can be atomic. The existing archive is never touched
            // until the new one is complete.
            var tempArchivePath = Path.Combine(
                _pathToFolder,
                $".{_nameZip}.{Guid.NewGuid():N}.tmp");

            try
            {
                var entries = await CompressAllAsync(paths, tempDir, progress, ct).ConfigureAwait(false);
                await WriteArchiveAsync(tempArchivePath, entries, ct).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();
                File.Move(tempArchivePath, archivePath, overwrite: _overwrite);

                return archivePath;
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(tempArchivePath);
                throw;
            }
            catch (AppException)
            {
                TryDeleteFile(tempArchivePath);
                throw;
            }
            catch (Exception ex)
            {
                TryDeleteFile(tempArchivePath);
                throw new AppException(
                    AppErrorCode.Add,
                    nameof(StartAsync),
                    "Failed to create archive.",
                    ex);
            }
            finally
            {
                TryDeleteFile(tempArchivePath);
                TryDeleteDirectory(tempDir);
            }
        }

        private async Task<CompressedEntry[]> CompressAllAsync(
            IReadOnlyList<string> paths,
            string tempDir,
            IProgress<ArchiveProgress>? progress,
            CancellationToken ct)
        {
            var entries = new CompressedEntry[paths.Count];
            var processed = 0;
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxWorkers,
                CancellationToken = ct
            };

            await Parallel.ForEachAsync(Enumerable.Range(0, paths.Count), options, async (index, token) =>
            {
                var entry = await CompressFileAsync(index, paths[index], tempDir, token).ConfigureAwait(false);
                entries[index] = entry;

                var done = Interlocked.Increment(ref processed);
                progress?.Report(new ArchiveProgress(done, paths.Count, paths[index]));
            }).ConfigureAwait(false);

            return entries;
        }

        private async Task<CompressedEntry> CompressFileAsync(
            int index,
            string sourcePath,
            string tempDir,
            CancellationToken ct)
        {
            if (!File.Exists(sourcePath))
            {
                throw new AppException(
                    AppErrorCode.Read,
                    nameof(CompressFileAsync),
                    $"File does not exist: {sourcePath}");
            }

            var tempPath = Path.Combine(tempDir, $"{index:D6}.deflate");
            var entryName = GetEntryName(sourcePath);
            var lastWriteTime = File.GetLastWriteTime(sourcePath);
            var crc = Crc32.Initial;
            long uncompressedSize = 0;

            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                await using var input = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                await using var output = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.Asynchronous);

                await using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
                {
                    int read;
                    while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
                    {
                        crc = Crc32.Append(crc, buffer.AsSpan(0, read));
                        uncompressedSize += read;
                        await deflate.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var compressedSize = new FileInfo(tempPath).Length;
            var (dosTime, dosDate) = ToDosDateTime(lastWriteTime);

            return new CompressedEntry(
                index,
                entryName,
                tempPath,
                Crc32.Finish(crc),
                uncompressedSize,
                compressedSize,
                dosTime,
                dosDate);
        }

        private async Task WriteArchiveAsync(string archivePath, CompressedEntry[] entries, CancellationToken ct)
        {
            await using var output = new FileStream(
                archivePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous);

            using var writer = new BinaryWriter(output, Utf8NoBom, leaveOpen: true);
            var centralEntries = new List<CentralEntry>(entries.Length);

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                var localOffset = output.Position;
                var nameBytes = Utf8NoBom.GetBytes(entry.EntryName);
                var sizeIsZip64 = entry.UncompressedSize >= uint.MaxValue || entry.CompressedSize >= uint.MaxValue;
                var versionNeeded = (ushort)(sizeIsZip64 ? 45 : 20);
                var extra = BuildZip64Extra(sizeIsZip64, entry.UncompressedSize, entry.CompressedSize, null);

                WriteLocalHeader(writer, entry, nameBytes, extra, versionNeeded, sizeIsZip64);
                writer.Flush();

                await using (var temp = new FileStream(
                    entry.TempPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await temp.CopyToAsync(output, ct).ConfigureAwait(false);
                }

                centralEntries.Add(new CentralEntry(
                    entry.EntryName,
                    entry.Crc,
                    entry.UncompressedSize,
                    entry.CompressedSize,
                    localOffset,
                    entry.DosTime,
                    entry.DosDate,
                    sizeIsZip64));
            }

            var centralDirectoryOffset = output.Position;
            foreach (var centralEntry in centralEntries)
            {
                WriteCentralHeader(writer, centralEntry);
            }

            writer.Flush();
            var centralDirectorySize = output.Position - centralDirectoryOffset;

            var needsZip64End = centralEntries.Count > ushort.MaxValue
                || centralDirectoryOffset >= uint.MaxValue
                || centralDirectorySize >= uint.MaxValue;

            if (needsZip64End)
            {
                WriteZip64EndOfCentralDirectory(
                    writer,
                    centralDirectoryOffset,
                    centralDirectorySize,
                    centralEntries.Count);
            }

            WriteEndOfCentralDirectory(
                writer,
                centralDirectoryOffset,
                centralDirectorySize,
                centralEntries.Count);

            writer.Flush();
            await output.FlushAsync(ct).ConfigureAwait(false);
        }

        private void ValidateFolders()
        {
            if (!Directory.Exists(_pathToFolder))
            {
                throw new AppException(
                    AppErrorCode.Open,
                    nameof(ValidateFolders),
                    $"Archive directory does not exist: {_pathToFolder}");
            }

            if (!Directory.Exists(_basePath))
            {
                throw new AppException(
                    AppErrorCode.Open,
                    nameof(ValidateFolders),
                    $"Source directory does not exist: {_basePath}");
            }
        }

        private string GetEntryName(string filePath)
        {
            var relativePath = Path.GetRelativePath(_basePath, filePath);
            if (relativePath == ".." || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                relativePath = Path.GetFileName(filePath);
            }

            return relativePath.Replace('\\', '/');
        }

        private static (ushort Time, ushort Date) ToDosDateTime(DateTime value)
        {
            if (value.Year < 1980)
            {
                value = new DateTime(1980, 1, 1);
            }

            var time = (ushort)((value.Hour << 11) | (value.Minute << 5) | (value.Second / 2));
            var date = (ushort)(((value.Year - 1980) << 9) | (value.Month << 5) | value.Day);
            return (time, date);
        }

        private static void WriteLocalHeader(
            BinaryWriter writer,
            CompressedEntry entry,
            byte[] name,
            byte[] extra,
            ushort versionNeeded,
            bool sizeIsZip64)
        {
            writer.Write(0x04034b50u);
            writer.Write(versionNeeded);
            writer.Write((ushort)0x0800); // UTF-8 file names
            writer.Write((ushort)8);      // Deflate
            writer.Write(entry.DosTime);
            writer.Write(entry.DosDate);
            writer.Write(entry.Crc);
            writer.Write(sizeIsZip64 ? uint.MaxValue : (uint)entry.CompressedSize);
            writer.Write(sizeIsZip64 ? uint.MaxValue : (uint)entry.UncompressedSize);
            writer.Write((ushort)name.Length);
            writer.Write((ushort)extra.Length);
            writer.Write(name);
            writer.Write(extra);
        }

        private static void WriteCentralHeader(BinaryWriter writer, CentralEntry entry)
        {
            var name = Utf8NoBom.GetBytes(entry.EntryName);
            var offsetIsZip64 = entry.LocalOffset >= uint.MaxValue;
            var extra = BuildZip64Extra(
                entry.SizeIsZip64,
                entry.UncompressedSize,
                entry.CompressedSize,
                offsetIsZip64 ? entry.LocalOffset : null);

            var versionNeeded = (ushort)(entry.SizeIsZip64 || offsetIsZip64 ? 45 : 20);
            var versionMadeBy = (ushort)((3 << 8) | versionNeeded); // Unix

            writer.Write(0x02014b50u);
            writer.Write(versionMadeBy);
            writer.Write(versionNeeded);
            writer.Write((ushort)0x0800); // UTF-8 file names
            writer.Write((ushort)8);      // Deflate
            writer.Write(entry.DosTime);
            writer.Write(entry.DosDate);
            writer.Write(entry.Crc);
            writer.Write(entry.SizeIsZip64 ? uint.MaxValue : (uint)entry.CompressedSize);
            writer.Write(entry.SizeIsZip64 ? uint.MaxValue : (uint)entry.UncompressedSize);
            writer.Write((ushort)name.Length);
            writer.Write((ushort)extra.Length);
            writer.Write((ushort)0); // comment length
            writer.Write((ushort)0); // disk number start
            writer.Write((ushort)0); // internal attributes
            writer.Write(0u);        // external attributes
            writer.Write(offsetIsZip64 ? uint.MaxValue : (uint)entry.LocalOffset);
            writer.Write(name);
            writer.Write(extra);
        }

        private static byte[] BuildZip64Extra(
            bool includeSizes,
            long uncompressedSize,
            long compressedSize,
            long? localHeaderOffset)
        {
            if (!includeSizes && localHeaderOffset is null)
            {
                return Array.Empty<byte>();
            }

            var size = (includeSizes ? 16 : 0) + (localHeaderOffset.HasValue ? 8 : 0);

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((ushort)0x0001); // ZIP64 extended information
            writer.Write((ushort)size);

            if (includeSizes)
            {
                writer.Write(uncompressedSize);
                writer.Write(compressedSize);
            }

            if (localHeaderOffset.HasValue)
            {
                writer.Write(localHeaderOffset.Value);
            }

            writer.Flush();
            return stream.ToArray();
        }

        private static void WriteZip64EndOfCentralDirectory(
            BinaryWriter writer,
            long centralDirectoryOffset,
            long centralDirectorySize,
            long entryCount)
        {
            var zip64EndOffset = writer.BaseStream.Position;

            writer.Write(0x06064b50u);
            writer.Write(44UL); // size of the remaining record
            writer.Write((ushort)((3 << 8) | 45));
            writer.Write((ushort)45);
            writer.Write(0u); // number of this disk
            writer.Write(0u); // disk with central directory
            writer.Write((ulong)entryCount);
            writer.Write((ulong)entryCount);
            writer.Write((ulong)centralDirectorySize);
            writer.Write((ulong)centralDirectoryOffset);

            // ZIP64 end of central directory locator
            writer.Write(0x07064b50u);
            writer.Write(0u);
            writer.Write((ulong)zip64EndOffset);
            writer.Write(1u);
        }

        private static void WriteEndOfCentralDirectory(
            BinaryWriter writer,
            long centralDirectoryOffset,
            long centralDirectorySize,
            long entryCount)
        {
            writer.Write(0x06054b50u);
            writer.Write((ushort)0); // number of this disk
            writer.Write((ushort)0); // disk with central directory
            writer.Write((ushort)Math.Min(entryCount, ushort.MaxValue));
            writer.Write((ushort)Math.Min(entryCount, ushort.MaxValue));
            writer.Write((uint)Math.Min(centralDirectorySize, uint.MaxValue));
            writer.Write((uint)Math.Min(centralDirectoryOffset, uint.MaxValue));
            writer.Write((ushort)0); // comment length
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

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
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

        private sealed record CompressedEntry(
            int Index,
            string EntryName,
            string TempPath,
            uint Crc,
            long UncompressedSize,
            long CompressedSize,
            ushort DosTime,
            ushort DosDate);

        private sealed record CentralEntry(
            string EntryName,
            uint Crc,
            long UncompressedSize,
            long CompressedSize,
            long LocalOffset,
            ushort DosTime,
            ushort DosDate,
            bool SizeIsZip64);
    }
}
