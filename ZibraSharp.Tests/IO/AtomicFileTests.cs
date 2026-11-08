using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ZibraSharp.Internal.IO;
using ZibraSharp.Tests.TestSupport;

namespace ZibraSharp.Tests.IO
{
    public sealed class AtomicFileTests
    {
        [Fact]
        public async Task WriteAsync_CreatesFileWithContent()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "nested", "state.json");

            await AtomicFile.WriteAsync(
                path,
                async (stream, ct) =>
                    await stream.WriteAsync(Encoding.UTF8.GetBytes("hello"), ct));

            Assert.True(File.Exists(path));
            Assert.Equal("hello", await File.ReadAllTextAsync(path));
        }

        [Fact]
        public async Task WriteAsync_OverwritesExistingFile()
        {
            using var temp = new TempDirectory();
            var path = temp.CreateFile("state.json", "old");

            await AtomicFile.WriteAsync(
                path,
                async (stream, ct) =>
                    await stream.WriteAsync(Encoding.UTF8.GetBytes("new"), ct));

            Assert.Equal("new", await File.ReadAllTextAsync(path));
        }

        [Fact]
        public async Task WriteAsync_KeepsExistingFile_WhenWriterThrows()
        {
            using var temp = new TempDirectory();
            var path = temp.CreateFile("state.json", "do not destroy");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                AtomicFile.WriteAsync(
                    path,
                    (stream, ct) =>
                    {
                        stream.WriteByte((byte)'x');
                        throw new InvalidOperationException("boom");
                    }));

            Assert.Equal("do not destroy", await File.ReadAllTextAsync(path));
        }

        [Fact]
        public async Task WriteAsync_DoesNotLeaveTemporaryFiles_WhenWriterThrows()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "state.json");

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                AtomicFile.WriteAsync(
                    path,
                    (stream, ct) => throw new InvalidOperationException("boom")));

            Assert.Empty(Directory.GetFiles(temp.Path));
        }

        [Fact]
        public async Task WriteAsync_DoesNotLeaveTemporaryFiles_OnSuccess()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "state.json");

            await AtomicFile.WriteAsync(
                path,
                async (stream, ct) =>
                    await stream.WriteAsync(Encoding.UTF8.GetBytes("hello"), ct));

            Assert.Single(Directory.GetFiles(temp.Path));
        }

        [Fact]
        public async Task WriteAsync_HonorsCancellation()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "state.json");
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                AtomicFile.WriteAsync(
                    path,
                    async (stream, ct) =>
                        await stream.WriteAsync(Encoding.UTF8.GetBytes("hello"), ct),
                    cts.Token));

            Assert.Empty(Directory.GetFiles(temp.Path));
        }

        [Fact]
        public async Task WriteAsync_RejectsEmptyPath()
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                AtomicFile.WriteAsync(" ", (stream, ct) => Task.CompletedTask));
        }

        [Fact]
        public async Task WriteAsync_RejectsNullWriter()
        {
            using var temp = new TempDirectory();

            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                AtomicFile.WriteAsync(Path.Combine(temp.Path, "state.json"), null!));
        }
    }
}
