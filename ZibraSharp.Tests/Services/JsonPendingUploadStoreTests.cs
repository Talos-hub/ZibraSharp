using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZibraSharp.Internal.Services;
using ZibraSharp.Tests.TestSupport;

namespace ZibraSharp.Tests.Services
{
    public sealed class JsonPendingUploadStoreTests
    {
        [Fact]
        public async Task AddGetRemove_RoundTrips()
        {
            using var temp = new TempDirectory();
            var store = new JsonPendingUploadStore(Path.Combine(temp.Path, "state", "pending.json"));

            await store.AddAsync(@"C:\backup\a.zip");
            await store.AddAsync(@"C:\backup\b.zip");

            var paths = await store.GetAllAsync();
            Assert.Equal(2, paths.Count);

            await store.RemoveAsync(@"C:\backup\a.zip");
            var remaining = await store.GetAllAsync();

            Assert.Equal(new[] { @"C:\backup\b.zip" }, remaining);
        }

        [Fact]
        public async Task Add_DoesNotDuplicatePathsIgnoringCase()
        {
            using var temp = new TempDirectory();
            var store = new JsonPendingUploadStore(Path.Combine(temp.Path, "pending.json"));

            await store.AddAsync(@"C:\backup\A.zip");
            await store.AddAsync(@"c:\backup\a.zip");

            var paths = await store.GetAllAsync();
            Assert.Single(paths);
        }

        [Fact]
        public async Task GetAll_ReturnsEmpty_WhenFileIsMissing()
        {
            using var temp = new TempDirectory();
            var store = new JsonPendingUploadStore(Path.Combine(temp.Path, "pending.json"));

            var paths = await store.GetAllAsync();

            Assert.Empty(paths);
        }

        [Fact]
        public async Task GetAll_ReturnsEmpty_WhenFileIsCorrupted()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "pending.json");
            await File.WriteAllTextAsync(path, "not json");
            var store = new JsonPendingUploadStore(path);

            var paths = await store.GetAllAsync();

            Assert.Empty(paths);
        }

        [Fact]
        public async Task Remove_OnEmptyStore_CreatesEmptyFile()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "pending.json");
            var store = new JsonPendingUploadStore(path);

            await store.RemoveAsync("a.zip");

            Assert.True(File.Exists(path));
            Assert.Empty(await store.GetAllAsync());
        }

        [Fact]
        public async Task ParallelAdds_ArePersistedWithoutLoss()
        {
            using var temp = new TempDirectory();
            var store = new JsonPendingUploadStore(Path.Combine(temp.Path, "pending.json"));

            var paths = Enumerable.Range(0, 50)
                .Select(index => Path.Combine(temp.Path, $"archive-{index}.zip"))
                .ToArray();

            await Task.WhenAll(paths.Select(path => store.AddAsync(path)));

            var stored = await store.GetAllAsync();
            Assert.Equal(50, stored.Count);
            Assert.Equal(paths.OrderBy(path => path), stored.OrderBy(path => path));
        }

        [Fact]
        public async Task Add_CreatesParentDirectory()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "deep", "nested", "pending.json");
            var store = new JsonPendingUploadStore(path);

            await store.AddAsync("a.zip");

            Assert.True(File.Exists(path));
        }
    }
}
