using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly IApiCloud _api;
        private readonly ILogger _logger;
        private readonly IPendingUploadStore _pendingUploads;

        public Service(
            IWalker walker,
            IArchiver archiver,
            IApiCloud api,
            ILogger logger,
            IPendingUploadStore pendingUploads)
        {
            _walker = walker ?? throw new ArgumentNullException(nameof(walker));
            _archiver = archiver ?? throw new ArgumentNullException(nameof(archiver));
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pendingUploads = pendingUploads ?? throw new ArgumentNullException(nameof(pendingUploads));
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

            return await TryUploadAsync(archivePath, ct).ConfigureAwait(false);
        }

        public Task<IReadOnlyList<string>> GetPendingAsync(CancellationToken ct = default)
        {
            return _pendingUploads.GetAllAsync(ct);
        }

        public async Task UploadPendingAsync(CancellationToken ct = default)
        {
            var pending = await _pendingUploads.GetAllAsync(ct).ConfigureAwait(false);
            if (pending.Count == 0)
            {
                _logger.Info("No pending archives to upload");
                return;
            }

            _logger.Info("Pending archives found", "Count", pending.Count);

            foreach (var archivePath in pending)
            {
                ct.ThrowIfCancellationRequested();

                if (!File.Exists(archivePath))
                {
                    _logger.Warn("Pending archive does not exist, removing from queue", "Path", archivePath);
                    await _pendingUploads.RemoveAsync(archivePath, ct).ConfigureAwait(false);
                    continue;
                }

                await TryUploadAsync(archivePath, ct).ConfigureAwait(false);
            }
        }

        private async Task<ServiceResult> TryUploadAsync(string archivePath, CancellationToken ct)
        {
            try
            {
                await _api.CheckAsync(ct).ConfigureAwait(false);
                await _api.UploadFileAsync(archivePath, ct).ConfigureAwait(false);

                _logger.Info("Upload completed", "Path", archivePath);

                await _pendingUploads.RemoveAsync(archivePath, ct).ConfigureAwait(false);
                TryDeleteArchive(archivePath);

                return new ServiceResult(ServiceStatus.Completed, archivePath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Upload failed, archive saved for later", "Path", archivePath);
                await _pendingUploads.AddAsync(archivePath, ct).ConfigureAwait(false);

                return new ServiceResult(ServiceStatus.PendingUpload, archivePath, ex);
            }
        }

        private void TryDeleteArchive(string archivePath)
        {
            try
            {
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                }
            }
            catch (IOException ex)
            {
                _logger.Warn("Failed to delete archive after upload", "Path", archivePath, "Error", ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.Warn("Failed to delete archive after upload", "Path", archivePath, "Error", ex.Message);
            }
        }
    }
}
