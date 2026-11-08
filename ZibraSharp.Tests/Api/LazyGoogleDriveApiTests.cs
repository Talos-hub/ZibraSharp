using System;
using System.Threading.Tasks;
using ZibraSharp.Internal.Api;
using ZibraSharp.Internal.Exceptions;
using ZibraSharp.Internal.Ports;
using ZibraSharp.Tests.TestSupport;

namespace ZibraSharp.Tests.Api
{
    public sealed class LazyGoogleDriveApiTests
    {
        [Fact]
        public async Task Factory_IsCalledOnce_ForRepeatedOperations()
        {
            var calls = 0;
            var inner = new FakeApiCloud();
            var lazy = new LazyGoogleDriveApi(_ =>
            {
                calls++;
                return Task.FromResult<IApiCloud>(inner);
            });

            await lazy.CheckAsync();
            await lazy.CheckAsync();

            Assert.Equal(1, calls);
            Assert.Equal(2, inner.CheckCalls);
        }

        [Fact]
        public async Task FactoryFailure_IsNotCached_AndNextCallRetries()
        {
            var calls = 0;
            var lazy = new LazyGoogleDriveApi(_ =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new AppException(AppErrorCode.Api, "Factory", "temporary");
                }

                return Task.FromResult<IApiCloud>(new FakeApiCloud());
            });

            await Assert.ThrowsAsync<AppException>(() => lazy.CheckAsync());
            await lazy.CheckAsync();

            Assert.Equal(2, calls);
        }

        [Fact]
        public async Task UploadFileAsync_DelegatesToInnerApi()
        {
            var inner = new FakeApiCloud();
            var lazy = new LazyGoogleDriveApi(_ => Task.FromResult<IApiCloud>(inner));

            await lazy.UploadFileAsync("backup.zip");

            Assert.Equal(new[] { "backup.zip" }, inner.UploadedPaths);
        }

        [Fact]
        public void Constructor_RejectsNullFactory()
        {
            Assert.Throws<ArgumentNullException>(() => new LazyGoogleDriveApi(null!));
        }
    }
}
