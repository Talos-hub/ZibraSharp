using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Exceptions;
using ZibraSharp.Internal.Ports;
using ZibraSharp.Internal.Services;
using ZibraSharp.Tests.TestSupport;

namespace ZibraSharp.Tests.Services
{
    public sealed class ServiceTests
    {
        [Fact]
        public async Task RunAsync_OnSuccess_UploadsAndDeletesArchive()
        {
            using var temp = new TempDirectory();
            var archivePath = Path.Combine(temp.Path, "out", "backup.zip");
            var logger = new FakeLogger();
            var api = new FakeApiCloud();
            var pending = new InMemoryPendingUploadStore();
            var walker = new FakeWalker { Paths = new[] { "a.txt", "b.txt" } };
            var archiver = new FakeArchiver { ArchivePath = archivePath };
            var service = new Service(walker, archiver, api, logger, pending);

            var result = await service.RunAsync();

            Assert.Equal(ServiceStatus.Completed, result.Status);
            Assert.Equal(archivePath, result.ArchivePath);
            Assert.Null(result.Error);
            Assert.False(File.Exists(archivePath));
            Assert.Empty(pending.Paths);
            Assert.Equal(new[] { archivePath }, api.UploadedPaths);
            Assert.Equal(new[] { "a.txt", "b.txt" }, archiver.ReceivedPaths);
            Assert.Contains(logger.Entries, entry => entry.Message == "Upload completed");
        }

        [Fact]
        public async Task RunAsync_WhenCloudIsUnavailable_KeepsArchiveAndSavesPending()
        {
            using var temp = new TempDirectory();
            var archivePath = Path.Combine(temp.Path, "out", "backup.zip");
            var error = new AppException(AppErrorCode.Api, "Check", "offline");
            var logger = new FakeLogger();
            var api = new FakeApiCloud { CheckException = error };
            var pending = new InMemoryPendingUploadStore();
            var walker = new FakeWalker { Paths = new[] { "a.txt" } };
            var archiver = new FakeArchiver { ArchivePath = archivePath };
            var service = new Service(walker, archiver, api, logger, pending);

            var result = await service.RunAsync();

            Assert.Equal(ServiceStatus.PendingUpload, result.Status);
            Assert.Same(error, result.Error);
            Assert.True(File.Exists(archivePath));
            Assert.Equal(new[] { archivePath }, pending.Paths);
            Assert.Empty(api.UploadedPaths);
            Assert.Contains(logger.Entries, entry =>
                entry.Level == "ERROR" && entry.Exception == error);
        }

        [Fact]
        public async Task RunAsync_WhenUploadFails_KeepsArchiveAndSavesPending()
        {
            using var temp = new TempDirectory();
            var archivePath = Path.Combine(temp.Path, "out", "backup.zip");
            var error = new AppException(AppErrorCode.Api, "Upload", "network");
            var api = new FakeApiCloud { UploadException = error };
            var pending = new InMemoryPendingUploadStore();
            var service = new Service(
                new FakeWalker { Paths = new[] { "a.txt" } },
                new FakeArchiver { ArchivePath = archivePath },
                api,
                new FakeLogger(),
                pending);

            var result = await service.RunAsync();

            Assert.Equal(ServiceStatus.PendingUpload, result.Status);
            Assert.Same(error, result.Error);
            Assert.True(File.Exists(archivePath));
            Assert.Equal(new[] { archivePath }, pending.Paths);
        }

        [Fact]
        public async Task RunAsync_WhenDirectoryIsEmpty_ThrowsAndDoesNotArchive()
        {
            using var temp = new TempDirectory();
            var logger = new FakeLogger();
            var walker = new FakeWalker { Paths = Array.Empty<string>() };
            var archiver = new FakeArchiver { ArchivePath = Path.Combine(temp.Path, "backup.zip") };
            var service = new Service(walker, archiver, new FakeApiCloud(), logger, new InMemoryPendingUploadStore());

            var exception = await Assert.ThrowsAsync<AppException>(() => service.RunAsync());

            Assert.Equal(AppErrorCode.EmptyDirectory, exception.Code);
            Assert.Equal(0, archiver.Calls);
            Assert.Contains(logger.Entries, entry => entry.Level == "WARN" && entry.Message == "Directory is empty");
        }

        [Fact]
        public async Task RunAsync_WhenArchiverFails_PropagatesError()
        {
            using var temp = new TempDirectory();
            var error = new AppException(AppErrorCode.Add, "Start", "archive failed");
            var archiver = new FakeArchiver { ArchivePath = Path.Combine(temp.Path, "backup.zip"), Exception = error };
            var service = new Service(
                new FakeWalker { Paths = new[] { "a.txt" } },
                archiver,
                new FakeApiCloud(),
                new FakeLogger(),
                new InMemoryPendingUploadStore());

            var exception = await Assert.ThrowsAsync<AppException>(() => service.RunAsync());

            Assert.Same(error, exception);
        }

        [Fact]
        public async Task RunAsync_WhenCancelledDuringUpload_PropagatesAndDoesNotSavePending()
        {
            using var temp = new TempDirectory();
            var archivePath = Path.Combine(temp.Path, "out", "backup.zip");
            var api = new FakeApiCloud { UploadException = new OperationCanceledException() };
            var pending = new InMemoryPendingUploadStore();
            var service = new Service(
                new FakeWalker { Paths = new[] { "a.txt" } },
                new FakeArchiver { ArchivePath = archivePath },
                api,
                new FakeLogger(),
                pending);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync());

            Assert.True(File.Exists(archivePath));
            Assert.Empty(pending.Paths);
        }

        [Fact]
        public async Task GetPendingAsync_ReturnsStoreContents()
        {
            var pending = new InMemoryPendingUploadStore();
            await pending.AddAsync("a.zip");
            await pending.AddAsync("b.zip");
            var service = CreateService(pending);

            var paths = await service.GetPendingAsync();

            Assert.Equal(new[] { "a.zip", "b.zip" }, paths);
        }

        [Fact]
        public async Task UploadPendingAsync_UploadsAndRemovesSuccessfulArchives()
        {
            using var temp = new TempDirectory();
            var first = temp.CreateFile("a.zip", "a");
            var second = temp.CreateFile("b.zip", "b");
            var pending = new InMemoryPendingUploadStore();
            await pending.AddAsync(first);
            await pending.AddAsync(second);
            var api = new FakeApiCloud();
            var service = CreateService(pending, api);

            await service.UploadPendingAsync();

            Assert.Equal(new[] { first, second }, api.UploadedPaths);
            Assert.Empty(pending.Paths);
            Assert.False(File.Exists(first));
            Assert.False(File.Exists(second));
        }

        [Fact]
        public async Task UploadPendingAsync_RemovesMissingFilesFromQueue()
        {
            using var temp = new TempDirectory();
            var missing = Path.Combine(temp.Path, "missing.zip");
            var pending = new InMemoryPendingUploadStore();
            await pending.AddAsync(missing);
            var api = new FakeApiCloud();
            var service = CreateService(pending, api);

            await service.UploadPendingAsync();

            Assert.Empty(pending.Paths);
            Assert.Empty(api.UploadedPaths);
        }

        [Fact]
        public async Task UploadPendingAsync_KeepsFailedArchives()
        {
            using var temp = new TempDirectory();
            var archive = temp.CreateFile("a.zip", "a");
            var pending = new InMemoryPendingUploadStore();
            await pending.AddAsync(archive);
            var api = new FakeApiCloud { UploadException = new AppException(AppErrorCode.Api, "Upload", "offline") };
            var service = CreateService(pending, api);

            await service.UploadPendingAsync();

            Assert.Equal(new[] { archive }, pending.Paths);
            Assert.True(File.Exists(archive));
        }

        [Fact]
        public void Constructor_RejectsNullDependencies()
        {
            var walker = new FakeWalker();
            var archiver = new FakeArchiver();
            var api = new FakeApiCloud();
            var logger = new FakeLogger();
            var pending = new InMemoryPendingUploadStore();

            Assert.Throws<ArgumentNullException>(() => new Service(null!, archiver, api, logger, pending));
            Assert.Throws<ArgumentNullException>(() => new Service(walker, null!, api, logger, pending));
            Assert.Throws<ArgumentNullException>(() => new Service(walker, archiver, null!, logger, pending));
            Assert.Throws<ArgumentNullException>(() => new Service(walker, archiver, api, null!, pending));
            Assert.Throws<ArgumentNullException>(() => new Service(walker, archiver, api, logger, null!));
        }

        private static Service CreateService(
            IPendingUploadStore pending,
            IApiCloud? api = null)
        {
            return new Service(
                new FakeWalker { Paths = Array.Empty<string>() },
                new FakeArchiver(),
                api ?? new FakeApiCloud(),
                new FakeLogger(),
                pending);
        }
    }
}
