using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Exceptions;
using ZibraSharp.Internal.Ports;
using ZibraSharp.Internal.Services;
using ZibraSharp.Tests.TestSupport;

namespace ZibraSharp.Tests.Services
{
    public sealed class PendingUploaderTests
    {
        [Fact]
        public async Task UploadAsync_OnSuccess_DeletesArchiveAndRemovesFromQueue()
        {
            using var temp = new TempDirectory();
            var archive = temp.CreateFile("a.zip", "a");
            var store = new InMemoryPendingUploadStore();
            await store.AddAsync(archive);
            var api = new FakeApiCloud();
            var uploader = new PendingUploader(api, new FakeLogger(), store);

            var result = await uploader.UploadAsync(archive);

            Assert.Equal(ServiceStatus.Completed, result.Status);
            Assert.False(File.Exists(archive));
            Assert.Empty(store.Paths);
            Assert.Equal(new[] { archive }, api.UploadedPaths);
        }

        [Fact]
        public async Task UploadAsync_OnFailure_KeepsArchiveAndAddsToQueue()
        {
            using var temp = new TempDirectory();
            var archive = temp.CreateFile("a.zip", "a");
            var store = new InMemoryPendingUploadStore();
            var error = new AppException(AppErrorCode.Api, "Upload", "offline");
            var uploader = new PendingUploader(
                new FakeApiCloud { UploadException = error },
                new FakeLogger(),
                store);

            var result = await uploader.UploadAsync(archive);

            Assert.Equal(ServiceStatus.PendingUpload, result.Status);
            Assert.Same(error, result.Error);
            Assert.True(File.Exists(archive));
            Assert.Equal(new[] { archive }, store.Paths);
        }

        [Fact]
        public async Task UploadAsync_OnCancellation_DoesNotAddToQueue()
        {
            using var temp = new TempDirectory();
            var archive = temp.CreateFile("a.zip", "a");
            var store = new InMemoryPendingUploadStore();
            var uploader = new PendingUploader(
                new FakeApiCloud { UploadException = new OperationCanceledException() },
                new FakeLogger(),
                store);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => uploader.UploadAsync(archive));

            Assert.True(File.Exists(archive));
            Assert.Empty(store.Paths);
        }

        [Fact]
        public async Task UploadPendingAsync_ReturnsEmpty_WhenQueueIsEmpty()
        {
            var uploader = new PendingUploader(
                new FakeApiCloud(),
                new FakeLogger(),
                new InMemoryPendingUploadStore());

            var results = await uploader.UploadPendingAsync();

            Assert.Empty(results);
        }

        [Fact]
        public async Task UploadPendingAsync_RemovesMissingFilesAndReturnsOnlyAttemptedResults()
        {
            using var temp = new TempDirectory();
            var store = new InMemoryPendingUploadStore();
            await store.AddAsync(Path.Combine(temp.Path, "missing.zip"));
            var uploader = new PendingUploader(new FakeApiCloud(), new FakeLogger(), store);

            var results = await uploader.UploadPendingAsync();

            Assert.Empty(results);
            Assert.Empty(store.Paths);
        }

        [Fact]
        public async Task UploadPendingAsync_ReportsMixedResults()
        {
            using var temp = new TempDirectory();
            var failing = temp.CreateFile("failing.zip", "f");
            var successful = temp.CreateFile("successful.zip", "s");
            var store = new InMemoryPendingUploadStore();
            await store.AddAsync(failing);
            await store.AddAsync(successful);

            var api = new FakeApiCloud
            {
                UploadExceptionFactory = path =>
                    path == failing
                        ? new AppException(AppErrorCode.Api, "Upload", "offline")
                        : null
            };

            var uploader = new PendingUploader(api, new FakeLogger(), store);
            var results = await uploader.UploadPendingAsync();

            Assert.Equal(2, results.Count);
            Assert.Single(results, result => result.Status == ServiceStatus.PendingUpload);
            Assert.Single(results, result => result.Status == ServiceStatus.Completed);
            Assert.Equal(new[] { failing }, store.Paths);
            Assert.True(File.Exists(failing));
            Assert.False(File.Exists(successful));
        }

        [Fact]
        public async Task UploadAsync_WithKeepArchive_KeepsUploadedArchive()
        {
            using var temp = new TempDirectory();
            var archive = temp.CreateFile("a.zip", "a");
            var store = new InMemoryPendingUploadStore();
            await store.AddAsync(archive);
            var uploader = new PendingUploader(
                new FakeApiCloud(),
                new FakeLogger(),
                store,
                keepArchive: true);

            var result = await uploader.UploadAsync(archive);

            Assert.Equal(ServiceStatus.Completed, result.Status);
            Assert.True(File.Exists(archive));
            Assert.Empty(store.Paths);
        }

        [Fact]
        public void Constructor_RejectsNullDependencies()
        {
            var api = new FakeApiCloud();
            var logger = new FakeLogger();
            var store = new InMemoryPendingUploadStore();

            Assert.Throws<ArgumentNullException>(() => new PendingUploader(null!, logger, store));
            Assert.Throws<ArgumentNullException>(() => new PendingUploader(api, null!, store));
            Assert.Throws<ArgumentNullException>(() => new PendingUploader(api, logger, null!));
        }
    }
}
