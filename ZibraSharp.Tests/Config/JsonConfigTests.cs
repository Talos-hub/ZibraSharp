using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ZibraSharp.Internal.Config;
using ZibraSharp.Internal.Exceptions;
using ZibraSharp.Tests.TestSupport;

namespace ZibraSharp.Tests.Configuration
{
    public sealed class JsonConfigTests
    {
        [Fact]
        public async Task SetZipDir_WritesGoCompatibleKey_AndRoundTrips()
        {
            using var temp = new TempDirectory();
            var config = new JsonConfig(temp.Path);

            await config.SetZipDirAsync(@"C:\backup\folder");

            var raw = await File.ReadAllTextAsync(Path.Combine(temp.Path, "zipdir.json"));
            using (var document = JsonDocument.Parse(raw))
            {
                Assert.Equal(@"C:\backup\folder", document.RootElement.GetProperty("zipdir").GetString());
            }

            var loaded = await config.GetDirAsync();
            Assert.NotNull(loaded);
            Assert.Equal(@"C:\backup\folder", loaded!.ZipDir);
        }

        [Fact]
        public async Task GetDir_ReturnsNull_WhenFileIsMissing()
        {
            using var temp = new TempDirectory();
            var config = new JsonConfig(temp.Path);

            var loaded = await config.GetDirAsync();

            Assert.Null(loaded);
        }

        [Fact]
        public async Task SetZipDir_RejectsEmptyPath()
        {
            using var temp = new TempDirectory();
            var config = new JsonConfig(temp.Path);

            var exception = await Assert.ThrowsAsync<AppException>(() => config.SetZipDirAsync(" "));

            Assert.Equal(AppErrorCode.Config, exception.Code);
        }

        [Fact]
        public async Task CreateExtensions_WritesAndReadsCaseInsensitively()
        {
            using var temp = new TempDirectory();
            var config = new JsonConfig(temp.Path);

            var created = await config.CreateExtensionsAsync(".EXE .txt .tar.gz");
            var loaded = await config.GetExtensionsAsync();

            Assert.Equal(3, created.Count);
            Assert.Equal(3, loaded.Count);
            Assert.Contains(".exe", loaded);
            Assert.Contains(".TXT", loaded);
            Assert.Contains(".tar.gz", loaded);
        }

        [Fact]
        public async Task GetExtensions_ReturnsEmpty_WhenFileIsMissing()
        {
            using var temp = new TempDirectory();
            var config = new JsonConfig(temp.Path);

            var extensions = await config.GetExtensionsAsync();

            Assert.Empty(extensions);
        }

        [Fact]
        public async Task GetExtensions_ReadsLegacyFileName()
        {
            using var temp = new TempDirectory();
            await File.WriteAllTextAsync(
                Path.Combine(temp.Path, "extentions.json"),
                """{".log": true, ".tmp": false}""");

            var config = new JsonConfig(temp.Path);
            var extensions = await config.GetExtensionsAsync();

            Assert.Single(extensions);
            Assert.Contains(".log", extensions);
        }

        [Fact]
        public async Task GetExtensions_ThrowsParseError_ForInvalidJson()
        {
            using var temp = new TempDirectory();
            await File.WriteAllTextAsync(Path.Combine(temp.Path, "extensions.json"), "not json");

            var config = new JsonConfig(temp.Path);
            var exception = await Assert.ThrowsAsync<AppException>(() => config.GetExtensionsAsync());

            Assert.Equal(AppErrorCode.Parse, exception.Code);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(".exe,.txt")]
        [InlineData("exe")]
        [InlineData(".")]
        public void ParseExtensions_RejectsInvalidInput(string input)
        {
            var exception = Assert.Throws<AppException>(() => JsonConfig.ParseExtensions(input));
            Assert.Equal(AppErrorCode.Config, exception.Code);
        }

        [Fact]
        public void ParseExtensions_AcceptsDotFilesAndMultiPartExtensions()
        {
            var extensions = JsonConfig.ParseExtensions(".gitignore .tar.gz .txt");

            Assert.Equal(3, extensions.Count);
            Assert.Contains(".gitignore", extensions);
            Assert.Contains(".tar.gz", extensions);
            Assert.Contains(".txt", extensions);
        }

        [Fact]
        public async Task RemoveExtensionsFile_DeletesCurrentAndLegacyFiles()
        {
            using var temp = new TempDirectory();
            var current = Path.Combine(temp.Path, "extensions.json");
            var legacy = Path.Combine(temp.Path, "extentions.json");
            await File.WriteAllTextAsync(current, "{}");
            await File.WriteAllTextAsync(legacy, "{}");

            var config = new JsonConfig(temp.Path);
            await config.RemoveExtensionsFileAsync();

            Assert.False(File.Exists(current));
            Assert.False(File.Exists(legacy));
        }
    }
}
