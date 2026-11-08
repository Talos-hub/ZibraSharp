using System.IO;
using System.Threading.Tasks;
using ZibraSharp.Internal.Api;
using ZibraSharp.Tests.TestSupport;

namespace ZibraSharp.Tests.Api
{
    public sealed class FileTokenStoreTests
    {
        private sealed record Token(string AccessToken, int ExpiresIn);

        [Fact]
        public async Task StoreAndGet_RoundTrips()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "state", "token.json");
            var store = new FileTokenStore(path);
            var token = new Token("secret", 3600);

            await store.StoreAsync("user", token);
            var loaded = await store.GetAsync<Token>("user");

            Assert.NotNull(loaded);
            Assert.Equal("secret", loaded!.AccessToken);
            Assert.Equal(3600, loaded.ExpiresIn);
            Assert.True(File.Exists(path));
        }

        [Fact]
        public async Task Get_ReturnsDefault_WhenFileIsMissing()
        {
            using var temp = new TempDirectory();
            var store = new FileTokenStore(Path.Combine(temp.Path, "token.json"));

            var loaded = await store.GetAsync<Token>("user");

            Assert.Null(loaded);
        }

        [Fact]
        public async Task Get_ReturnsDefault_WhenFileIsCorrupted()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "token.json");
            await File.WriteAllTextAsync(path, "not json");
            var store = new FileTokenStore(path);

            var loaded = await store.GetAsync<Token>("user");

            Assert.Null(loaded);
        }

        [Fact]
        public async Task Delete_RemovesTokenFile()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "token.json");
            var store = new FileTokenStore(path);
            await store.StoreAsync("user", new Token("secret", 3600));

            await store.DeleteAsync<Token>("user");

            Assert.False(File.Exists(path));
        }

        [Fact]
        public async Task Clear_RemovesTokenFile()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "token.json");
            var store = new FileTokenStore(path);
            await store.StoreAsync("user", new Token("secret", 3600));

            await store.ClearAsync();

            Assert.False(File.Exists(path));
        }

        [Fact]
        public async Task ConcurrentStores_LeaveValidJson()
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "token.json");
            var store = new FileTokenStore(path);

            await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
                store.StoreAsync("user", new Token($"token-{index}", index))));

            var loaded = await store.GetAsync<Token>("user");

            Assert.NotNull(loaded);
            Assert.StartsWith("token-", loaded!.AccessToken);
        }
    }
}
