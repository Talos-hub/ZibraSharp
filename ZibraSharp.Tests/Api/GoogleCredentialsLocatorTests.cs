using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZibraSharp.Internal.Api;
using ZibraSharp.Internal.Exceptions;
using ZibraSharp.Tests.TestSupport;

namespace ZibraSharp.Tests.Api
{
    public sealed class GoogleCredentialsLocatorTests
    {
        [Fact]
        public async Task LoadAsync_ReturnsContent_ForValidJson()
        {
            using var temp = new TempDirectory();
            var path = temp.CreateFile("credentials.json", """{"installed":{"client_id":"test"}}""");

            var content = await GoogleCredentialsLocator.LoadAsync(path);

            Assert.NotEmpty(content);
            Assert.Equal(path, GoogleCredentialsLocator.Find(path));
        }

        [Fact]
        public async Task LoadAsync_ThrowsParseError_ForInvalidJson()
        {
            using var temp = new TempDirectory();
            var path = temp.CreateFile("credentials.json", "not json");

            var exception = await Assert.ThrowsAsync<AppException>(() => GoogleCredentialsLocator.LoadAsync(path));

            Assert.Equal(AppErrorCode.Parse, exception.Code);
        }

        [Fact]
        public void GetCandidates_PutsExplicitPathFirst()
        {
            var candidates = GoogleCredentialsLocator.GetCandidates("custom.json");

            Assert.Equal("custom.json", candidates[0]);
        }

        [Fact]
        public void GetCandidates_ContainsDefaultAndHomeLocations()
        {
            var candidates = GoogleCredentialsLocator.GetCandidates();

            Assert.Contains("credentials.json", candidates);
            Assert.Contains(Path.Combine("config", "credentials.json"), candidates);
            Assert.Contains(
                candidates,
                path => path.Contains(".ZibraSharp", StringComparison.OrdinalIgnoreCase));
        }
    }
}
