using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Config;
using ZibraSharp.Internal.Exceptions;
using ZibraSharp.Internal.Walkers;
using ZibraSharp.Tests.TestSupport;

namespace ZibraSharp.Tests.Walking
{
    public sealed class WalkerTests
    {
        [Fact]
        public async Task Walk_ReturnsAllFilesRecursively()
        {
            using var temp = new TempDirectory();
            var root = temp.CreateDirectory("root");
            var first = temp.CreateFile(Path.Combine("root", "a.txt"), "a");
            var second = temp.CreateFile(Path.Combine("root", "sub", "b.txt"), "b");
            var third = temp.CreateFile(Path.Combine("root", "sub", "deeper", "c.md"), "c");

            var walker = new Walker(root, new JsonConfig(temp.Path));
            var paths = await walker.WalkAsync();

            Assert.Equal(3, paths.Count);
            Assert.Contains(first, paths);
            Assert.Contains(second, paths);
            Assert.Contains(third, paths);
        }

        [Fact]
        public async Task Walk_ReturnsEmpty_WhenDirectoryIsEmpty()
        {
            using var temp = new TempDirectory();
            var root = temp.CreateDirectory("root");

            var walker = new Walker(root, new JsonConfig(temp.Path));
            var paths = await walker.WalkAsync();

            Assert.Empty(paths);
        }

        [Fact]
        public async Task Walk_FiltersExtensions_CaseInsensitively()
        {
            using var temp = new TempDirectory();
            var root = temp.CreateDirectory("root");
            temp.CreateFile(Path.Combine("root", "keep.md"), "keep");
            temp.CreateFile(Path.Combine("root", "skip.TXT"), "skip");
            temp.CreateFile(Path.Combine("root", "sub", "skip.bin"), "skip");

            var config = new JsonConfig(temp.Path);
            await config.CreateExtensionsAsync(".txt .bin");

            var walker = new Walker(root, config);
            var paths = await walker.WalkAsync();

            Assert.Single(paths);
            Assert.EndsWith("keep.md", paths[0]);
        }

        [Fact]
        public async Task Walk_KeepsFilesWithoutExtension()
        {
            using var temp = new TempDirectory();
            var root = temp.CreateDirectory("root");
            temp.CreateFile(Path.Combine("root", "Makefile"), "keep");

            var config = new JsonConfig(temp.Path);
            await config.CreateExtensionsAsync(".txt");

            var walker = new Walker(root, config);
            var paths = await walker.WalkAsync();

            Assert.Single(paths);
            Assert.EndsWith("Makefile", paths[0]);
        }

        [Fact]
        public async Task Walk_MatchesMultiPartExtension()
        {
            using var temp = new TempDirectory();
            var root = temp.CreateDirectory("root");
            temp.CreateFile(Path.Combine("root", "archive.tar.gz"), "skip");
            temp.CreateFile(Path.Combine("root", "archive.gz"), "keep");

            var config = new JsonConfig(temp.Path);
            await config.CreateExtensionsAsync(".tar.gz");

            var walker = new Walker(root, config);
            var paths = await walker.WalkAsync();

            Assert.Single(paths);
            Assert.EndsWith("archive.gz", paths[0]);
        }

        [Fact]
        public async Task Walk_Throws_WhenDirectoryDoesNotExist()
        {
            using var temp = new TempDirectory();
            var missing = Path.Combine(temp.Path, "missing");

            var walker = new Walker(missing, new JsonConfig(temp.Path));
            var exception = await Assert.ThrowsAsync<AppException>(() => walker.WalkAsync());

            Assert.Equal(AppErrorCode.Open, exception.Code);
        }

        [Fact]
        public async Task Walk_Throws_WhenPathIsAFile()
        {
            using var temp = new TempDirectory();
            var file = temp.CreateFile("file.txt", "content");

            var walker = new Walker(file, new JsonConfig(temp.Path));
            var exception = await Assert.ThrowsAsync<AppException>(() => walker.WalkAsync());

            Assert.Equal(AppErrorCode.Open, exception.Code);
        }

        [Fact]
        public async Task Walk_HonorsCancellation()
        {
            using var temp = new TempDirectory();
            var root = temp.CreateDirectory("root");
            temp.CreateFile(Path.Combine("root", "a.txt"), "a");

            var walker = new Walker(root, new JsonConfig(temp.Path));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => walker.WalkAsync(cts.Token));
        }

        [Fact]
        public async Task Walk_PropagatesConfigErrors()
        {
            using var temp = new TempDirectory();
            var root = temp.CreateDirectory("root");
            temp.CreateFile(Path.Combine("root", "a.txt"), "a");
            await File.WriteAllTextAsync(Path.Combine(temp.Path, "extensions.json"), "broken");

            var walker = new Walker(root, new JsonConfig(temp.Path));
            var exception = await Assert.ThrowsAsync<AppException>(() => walker.WalkAsync());

            Assert.Equal(AppErrorCode.Parse, exception.Code);
        }
    }
}
