using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Ports;

namespace ZibraSharp.Internal.Services
{
    /// <summary>
    /// Uploads archives and manages the pending-upload queue.
    /// Kept separate from <see cref="Service"/> so the CLI can retry pending
    /// archives without constructing a walker or an archiver.
    /// </summary>
    public sealed class PendingUploader
    {
        private readonly IApiCloud _api;
        private readonly ILogger _logger;
        private readonly IPendingUploadStore _store;

        public PendingUploader(IApiCloud api, ILogger logger, IPendingUploadStore store)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public Task<IReadOnlyList<string>> GetPendingAsync(CancellationToken ct = default)
        {
            return _store.GetAllAsync(ct);
        }

        /// <summary>
        /// Tries to upload one archive. On failure the archive is kept and added
        /// to the pending queue (callback mechanism).
        /// </summary>
        public async Task<ServiceResult> UploadAsync(string archivePath, CancellationToken ct = default)
        {
            try
            {
                await _api.CheckAsync(ct).ConfigureAwait(false);
                await _api.UploadFileAsync(archivePath, ct).ConfigureAwait(false);

                _logger.Info("Upload completed", "Path", archivePath);

                await _store.RemoveAsync(archivePath, ct).ConfigureAwait(false);
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
                await _store.AddAsync(archivePath, ct).ConfigureAwait(false);

                return new ServiceResult(ServiceStatus.PendingUpload, archivePath, ex);
            }
        }

        /// <summary>
        /// Tries to upload every pending archive. Missing files are removed from the queue.
        /// </summary>
        /// <returns>Results for archives that were actually attempted.</returns>
        public async Task<IReadOnlyList<ServiceResult>> UploadPendingAsync(CancellationToken ct = default)
        {
            var pending = await _store.GetAllAsync(ct).ConfigureAwait(false);
            if (pending.Count == 0)
            {
                _logger.Info("No pending archives to upload");
                return Array.Empty<ServiceResult>();
            }

            _logger.Info("Pending archives found", "Count", pending.Count);
            var results = new List<ServiceResult>(pending.Count);

            foreach (var archivePath in pending)
            {
                ct.ThrowIfCancellationRequested();

                if (!File.Exists(archivePath))
                {
                    _logger.Warn("Pending archive does not exist, removing from queue", "Path", archivePath);
                    await _store.RemoveAsync(archivePath, ct).ConfigureAwait(false);
                    continue;
                }

                results.Add(await UploadAsync(archivePath, ct).ConfigureAwait(false));
            }

            return results;
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
