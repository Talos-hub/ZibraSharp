using System;
using System.IO;

namespace ZibraSharp.Benchmarks
{
    /// <summary>
    /// Isolated temporary directory used by benchmark setups.
    /// </summary>
    internal sealed class BenchmarkWorkspace : IDisposable
    {
        public BenchmarkWorkspace()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ZibraSharp.Benchmarks",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateDirectory(string name)
        {
            var fullPath = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
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
