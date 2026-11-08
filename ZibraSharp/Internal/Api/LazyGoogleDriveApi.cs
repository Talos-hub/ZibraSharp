using System;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Ports;

namespace ZibraSharp.Internal.Api
{
    /// <summary>
    /// Creates the real cloud client on first use. This is important for the
    /// callback feature: the archive must be created even when the network (or
    /// the OAuth flow) is unavailable at startup.
    /// </summary>
    public sealed class LazyGoogleDriveApi : IApiCloud
    {
        private readonly Func<CancellationToken, Task<IApiCloud>> _factory;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private IApiCloud? _instance;

        public LazyGoogleDriveApi(Func<CancellationToken, Task<IApiCloud>> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public async Task CheckAsync(CancellationToken ct = default)
        {
            var api = await GetAsync(ct).ConfigureAwait(false);
            await api.CheckAsync(ct).ConfigureAwait(false);
        }

        public async Task UploadFileAsync(string filePath, CancellationToken ct = default)
        {
            var api = await GetAsync(ct).ConfigureAwait(false);
            await api.UploadFileAsync(filePath, ct).ConfigureAwait(false);
        }

        private async Task<IApiCloud> GetAsync(CancellationToken ct)
        {
            if (_instance is not null)
            {
                return _instance;
            }

            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_instance is null)
                {
                    _instance = await _factory(ct).ConfigureAwait(false);
                }

                return _instance;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
