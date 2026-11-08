using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Exceptions;
using ZibraSharp.Internal.Ports;

namespace ZibraSharp.Internal.Services
{
    /// <summary>
    /// Orchestrates the backup flow: scan → archive → upload.
    /// If the cloud is unavailable the archive is not deleted; its path is stored
    /// in <see cref="IPendingUploadStore"/> so the next run can offer a retry.
    /// </summary>
    public sealed class Service : IService
    {
        private readonly IWalker _walker;
        private readonly IArchiver _archiver;
        private readonly ILogger _logger;
        private readonly PendingUploader _pendingUploader;

        public Service(
            IWalker walker,
            IArchiver archiver,
            IApiCloud api,
            ILogger logger,
            IPendingUploadStore pendingUploads)
        {
            _walker = walker ?? throw new ArgumentNullException(nameof(walker));
            _archiver = archiver ?? throw new ArgumentNullException(nameof(archiver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (api is null)
            {
                throw new ArgumentNullException(nameof(api));
            }

            if (pendingUploads is null)
            {
                throw new ArgumentNullException(nameof(pendingUploads));
            }

            _pendingUploader = new PendingUploader(api, logger, pendingUploads);
        }

        public async Task<ServiceResult> RunAsync(CancellationToken ct = default)
        {
            var paths = await _walker.WalkAsync(ct).ConfigureAwait(false);
            if (paths.Count == 0)
            {
                _logger.Warn("Directory is empty");
                throw new AppException(
                    AppErrorCode.EmptyDirectory,
                    nameof(RunAsync),
                    "Directory is empty.");
            }

            _logger.Info("Files found", "Count", paths.Count);

            var archivePath = await _archiver.StartAsync(paths, ct).ConfigureAwait(false);
            _logger.Info("Archive created", "Path", archivePath);

            return await _pendingUploader.UploadAsync(archivePath, ct).ConfigureAwait(false);
        }

        public Task<IReadOnlyList<string>> GetPendingAsync(CancellationToken ct = default)
        {
            return _pendingUploader.GetPendingAsync(ct);
        }

        public async Task UploadPendingAsync(CancellationToken ct = default)
        {
            await _pendingUploader.UploadPendingAsync(ct).ConfigureAwait(false);
        }
    }
}
