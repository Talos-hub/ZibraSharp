using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Archivers;
using ZibraSharp.Internal.Exceptions;
using ZibraSharp.Tests.TestSupport;

namespace ZibraSharp.Tests.Archiving
{
    public sealed class ZipArchiverTests
    {
        [Fact]
        public async Task StartAsync_CreatesValidZip_WithRelativePathsAndContent()
        {
            using var temp = new TempDirectory();
            var source = temp.CreateDirectory("source");
            var output = temp.CreateDirectory("output");
            var first = temp.CreateFile(Path.Combine("source", "a.txt"), "hello");
            var second = temp.CreateFile(Path.Combine("source", "sub", "b.txt"), "world");

            var archiver = new ZipArchiver(output, source, "backup.zip", maxWorkers: 4);
            var archivePath = await archiver.StartAsync(new[] { first, second });

            Assert.True(File.Exists(archivePath));

            using var zip = ZipFile.OpenRead(archivePath);
            Assert.Equal(2, zip.Entries.Count);
            Assert.Equal("hello", ReadEntry(zip, "a.txt"));
            Assert.Equal("world", ReadEntry(zip, "sub/b.txt"));
        }

        [Fact]
        public async Task StartAsync_PreservesUnicodeFileNames()
        {
            using var temp = new TempDirectory();
            var source = temp.CreateDirectory("source");
            var output = temp.CreateDirectory("output");
            var file = temp.CreateFile(Path.Combine("source", "привет-мир.txt"), "unicode");

            var archiver = new ZipArchiver(output, source, "backup.zip");
            var archivePath = await archiver.StartAsync(new[] { file });

            using var zip = ZipFile.OpenRead(archivePath);
            Assert.Equal("unicode", ReadEntry(zip, "привет-мир.txt"));
        }

        [Fact]
        public async Task StartAsync_HandlesEmptyFiles()
        {
            using var temp = new TempDirectory();
            var source = temp.CreateDirectory("source");
            var output = temp.CreateDirectory("output");
            var file = temp.CreateFile(Path.Combine("source", "empty.txt"), string.Empty);

            var archiver = new ZipArchiver(output, source, "backup.zip");
            var archivePath = await archiver.StartAsync(new[] { file });

            using var zip = ZipFile.OpenRead(archivePath);
            var entry = Assert.Single(zip.Entries);
            Assert.Equal(0, entry.Length);
        }

        [Fact]
        public async Task StartAsync_CompressesData()
        {
            using var temp = new TempDirectory();
            var source = temp.CreateDirectory("source");
            var output = temp.CreateDirectory("output");
            var content = new string('x', 100_000);
            var file = temp.CreateFile(Path.Combine("source", "compressible.txt"), content);

            var archiver = new ZipArchiver(output, source, "backup.zip");
            var archivePath = await archiver.StartAsync(new[] { file });

            var archiveSize = new FileInfo(archivePath).Length;
            Assert.True(archiveSize < content.Length / 10, $"Archive is too large: {archiveSize} bytes.");
        }

        [Fact]
        public async Task StartAsync_ArchivesManyFilesInParallel()
        {
            using var temp = new TempDirectory();
            var source = temp.CreateDirectory("source");
            var output = temp.CreateDirectory("output");

            var files = new List<string>();
            for (var i = 0; i < 100; i++)
            {
                files.Add(temp.CreateFile(
                    Path.Combine("source", "nested", $"file-{i:D3}.txt"),
                    $"content-{i}"));
            }

            var archiver = new ZipArchiver(output, source, "backup.zip", maxWorkers: 8);
            var archivePath = await archiver.StartAsync(files);

            using var zip = ZipFile.OpenRead(archivePath);
            Assert.Equal(100, zip.Entries.Count);

            for (var i = 0; i < 100; i++)
            {
                Assert.Equal($"content-{i}", ReadEntry(zip, $"nested/file-{i:D3}.txt"));
            }
        }

        [Fact]
        public async Task StartAsync_Throws_WhenNoPathsProvided()
        {
            using var temp = new TempDirectory();
            var source = temp.CreateDirectory("source");
            var output = temp.CreateDirectory("output");
            var archiver = new ZipArchiver(output, source, "backup.zip");

            var exception = await Assert.ThrowsAsync<AppException>(() => archiver.StartAsync(Array.Empty<string>()));

            Assert.Equal(AppErrorCode.EmptyDirectory, exception.Code);
        }

        [Fact]
        public async Task StartAsync_Throws_WhenArchiveAlreadyExists()
        {
            using var temp = new TempDirectory();
            var source = temp.CreateDirectory("source");
            var output = temp.CreateDirectory("output");
            var file = temp.CreateFile(Path.Combine("source", "a.txt"), "a");
            var existingArchive = Path.Combine(output, "backup.zip");
            await File.WriteAllTextAsync(existingArchive, "do not overwrite");

            var archiver = new ZipArchiver(output, source, "backup.zip");
            var exception = await Assert.ThrowsAsync<AppException>(() => archiver.StartAsync(new[] { file }));

            Assert.Equal(AppErrorCode.Create, exception.Code);
            Assert.Equal("do not overwrite", await File.ReadAllTextAsync(existingArchive));
        }

        [Fact]
        public async Task StartAsync_ThrowsAndCleansUp_WhenSourceFileIsMissing()
        {
            using var temp = new TempDirectory();
            var source = temp.CreateDirectory("source");
            var output = temp.CreateDirectory("output");
            var missing = Path.Combine(source, "missing.txt");

            var archiver = new ZipArchiver(output, source, "backup.zip");
            var exception = await Assert.ThrowsAsync<AppException>(() => archiver.StartAsync(new[] { missing }));

            Assert.Equal(AppErrorCode.Read, exception.Code);
            Assert.False(File.Exists(Path.Combine(output, "backup.zip")));
        }

        [Fact]
        public async Task StartAsync_Throws_WhenSourceDirectoryDoesNotExist()
        {
            using var temp = new TempDirectory();
            var output = temp.CreateDirectory("output");
            var missingSource = Path.Combine(temp.Path, "missing");
            var file = temp.CreateFile("file.txt", "content");

            var archiver = new ZipArchiver(output, missingSource, "backup.zip");
            var exception = await Assert.ThrowsAsync<AppException>(() => archiver.StartAsync(new[] { file }));

            Assert.Equal(AppErrorCode.Open, exception.Code);
        }

        [Fact]
        public async Task StartAsync_HonorsCancellation()
        {
            using var temp = new TempDirectory();
            var source = temp.CreateDirectory("source");
            var output = temp.CreateDirectory("output");
            var file = temp.CreateFile(Path.Combine("source", "a.txt"), "a");

            var archiver = new ZipArchiver(output, source, "backup.zip");
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => archiver.StartAsync(new[] { file }, cts.Token));
            Assert.False(File.Exists(Path.Combine(output, "backup.zip")));
        }

        private static string ReadEntry(ZipArchive zip, string name)
        {
            var entry = zip.GetEntry(name);
            Assert.NotNull(entry);
            using var reader = new StreamReader(entry!.Open());
            return reader.ReadToEnd();
        }
    }
}
