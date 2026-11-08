using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZibraSharp.Internal.Config;
using ZibraSharp.Internal.Exceptions;

namespace ZibraSharp.Internal.Ports
{
    /// <summary>
    /// Walks a directory and returns paths to files.
    /// </summary>
    public interface IWalker
    {
        /// <summary>
        /// Walks the configured directory and returns paths to all files.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Materialized collection of file paths.</returns>
        Task<IReadOnlyList<string>> WalkAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Creates an archive from the given files.
    /// </summary>
    public interface IArchiver
    {
        /// <summary>
        /// Creates a new archive and adds the given files to it.
        /// </summary>
        /// <param name="paths">Paths to the files that must be archived.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <param name="progress">Optional per-file progress reporter.</param>
        /// <returns>Full path to the created archive.</returns>
        Task<string> StartAsync(
            IReadOnlyList<string> paths,
            CancellationToken ct = default,
            IProgress<ArchiveProgress>? progress = null);
    }

    /// <summary>
    /// Provides the logging logic for the application.
    /// </summary>
    public interface ILogger
    {
        void Info(string message, params object?[] args);
        void Warn(string message, params object?[] args);
        void Debug(string message, params object?[] args);
        void Error(string message, params object?[] args);
        void Error(Exception exception, string message, params object?[] args);
    }

    /// <summary>
    /// Provides logic for working with the cloud — Google Drive or another cloud platform.
    /// </summary>
    public interface IApiCloud
    {
        /// <summary>
        /// Checks that the cloud is reachable and the credentials are valid.
        /// </summary>
        /// <exception cref="AppException">Thrown when the cloud is unavailable.</exception>
        Task CheckAsync(CancellationToken ct = default);

        /// <summary>
        /// Uploads a file to the cloud.
        /// </summary>
        /// <param name="filePath">Full path to the file to upload.</param>
        /// <param name="ct">Cancellation token.</param>
        Task UploadFileAsync(string filePath, CancellationToken ct = default);
    }

    /// <summary>
    /// Stores archives that could not be uploaded (callback mechanism).
    /// On the next application run these archives are offered for re-upload.
    /// </summary>
    public interface IPendingUploadStore
    {
        Task<IReadOnlyList<string>> GetAllAsync(CancellationToken ct = default);
        Task AddAsync(string archivePath, CancellationToken ct = default);
        Task RemoveAsync(string archivePath, CancellationToken ct = default);
    }

    /// <summary>
    /// Provides configuration for the application.
    /// </summary>
    public interface IConfig
    {
        /// <summary>
        /// Loads the backup directory from zipdir.json.
        /// </summary>
        /// <returns>Configuration or null when the file does not exist.</returns>
        Task<ZipDirConfig?> GetDirAsync(CancellationToken ct = default);

        /// <summary>
        /// Writes the backup directory to zipdir.json.
        /// </summary>
        Task SetZipDirAsync(string path, CancellationToken ct = default);

        /// <summary>
        /// Loads excluded extensions. A missing file means "no exclusions".
        /// </summary>
        Task<IReadOnlySet<string>> GetExtensionsAsync(CancellationToken ct = default);

        /// <summary>
        /// Parses user input (for example ".exe .txt") and writes extensions.json.
        /// </summary>
        Task<IReadOnlySet<string>> CreateExtensionsAsync(string input, CancellationToken ct = default);

        /// <summary>
        /// Deletes the extensions file.
        /// </summary>
        Task RemoveExtensionsFileAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Orchestrates the backup flow: scan → archive → upload.
    /// </summary>
    public interface IService
    {
        /// <summary>
        /// Runs the full backup flow. If the cloud is unavailable the archive is kept
        /// and registered in <see cref="IPendingUploadStore"/> for a later retry.
        /// </summary>
        Task<ServiceResult> RunAsync(
            CancellationToken ct = default,
            IProgress<ArchiveProgress>? progress = null);

        /// <summary>
        /// Returns archives waiting for upload.
        /// </summary>
        Task<IReadOnlyList<string>> GetPendingAsync(CancellationToken ct = default);

        /// <summary>
        /// Tries to upload all pending archives.
        /// </summary>
        Task UploadPendingAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Result of a backup run.
    /// </summary>
    public enum ServiceStatus
    {
        /// <summary>Archive was created and uploaded successfully.</summary>
        Completed,

        /// <summary>Archive was created but the upload failed; it is saved for later.</summary>
        PendingUpload
    }

    public sealed record ServiceResult(ServiceStatus Status, string ArchivePath, Exception? Error = null);

    /// <summary>
    /// Progress of the archiving stage.
    /// </summary>
    public sealed record ArchiveProgress(int Processed, int Total, string FilePath);
}
