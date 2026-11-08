using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZibraSharp.Internal.IO
{
    /// <summary>
    /// Writes files atomically. Data is written to a temporary file in the same
    /// directory and then moved over the destination, so readers never observe a
    /// half-written file and a crash cannot corrupt existing state.
    /// </summary>
    public static class AtomicFile
    {
        public static async Task WriteAsync(
            string path,
            Func<Stream, CancellationToken, Task> write,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("File path must not be empty.", nameof(path));
            }

            if (write is null)
            {
                throw new ArgumentNullException(nameof(write));
            }

            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = Path.Combine(
                directory ?? ".",
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                await using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous))
                {
                    await write(stream, ct).ConfigureAwait(false);
                    await stream.FlushAsync(ct).ConfigureAwait(false);
                }

                File.Move(tempPath, fullPath, overwrite: true);
            }
            finally
            {
                TryDelete(tempPath);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort cleanup.
            }
        }
    }
}
