using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Drive.v3;
using Google.Apis.Upload;
using ZibraSharp.Internal.Exceptions;
using ZibraSharp.Internal.Ports;

namespace ZibraSharp.Internal.Api
{
    /// <summary>
    /// Google Drive implementation of <see cref="IApiCloud"/>.
    /// </summary>
    public sealed class GoogleDriveApi : IApiCloud
    {
        private const string UploadContentType = "application/zip";

        private readonly DriveService _service;

        public GoogleDriveApi(DriveService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public async Task CheckAsync(CancellationToken ct = default)
        {
            try
            {
                // A cheap Drive API call that is allowed by the drive.file scope.
                // About.Get may require broader scopes, so list one file instead.
                var request = _service.Files.List();
                request.PageSize = 1;
                request.Fields = "files(id)";
                await request.ExecuteAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AppException(
                    AppErrorCode.Api,
                    nameof(CheckAsync),
                    "Cannot connect to Google Drive.",
                    ex);
            }
        }

        public async Task UploadFileAsync(string filePath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new AppException(
                    AppErrorCode.Upload,
                    nameof(UploadFileAsync),
                    "File path must not be empty.");
            }

            if (!File.Exists(filePath))
            {
                throw new AppException(
                    AppErrorCode.Read,
                    nameof(UploadFileAsync),
                    $"File does not exist: {filePath}");
            }

            try
            {
                await using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                var metadata = new Google.Apis.Drive.v3.Data.File
                {
                    Name = Path.GetFileName(filePath)
                };

                var request = _service.Files.Create(metadata, stream, UploadContentType);
                var progress = await request.UploadAsync(ct).ConfigureAwait(false);

                if (progress.Status == UploadStatus.Failed)
                {
                    throw new AppException(
                        AppErrorCode.Api,
                        nameof(UploadFileAsync),
                        $"Failed to upload file to Google Drive: {filePath}",
                        progress.Exception);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AppException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AppException(
                    AppErrorCode.Api,
                    nameof(UploadFileAsync),
                    $"Failed to upload file to Google Drive: {filePath}",
                    ex);
            }
        }
    }
}
