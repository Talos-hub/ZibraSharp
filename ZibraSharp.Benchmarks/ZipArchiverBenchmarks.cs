using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using ZibraSharp.Internal.Archivers;

namespace ZibraSharp.Benchmarks
{
    /// <summary>
    /// Measures the parallel ZIP archiver with different worker counts.
    /// </summary>
    [MemoryDiagnoser]
    public class ZipArchiverBenchmarks
    {
        private const int FileCount = 100;
        private const int FileSizeBytes = 32 * 1024;

        private BenchmarkWorkspace _workspace = null!;
        private ZipArchiver _archiver = null!;
        private IReadOnlyList<string> _paths = null!;

        [Params(1, 4)]
        public int Workers { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _workspace = new BenchmarkWorkspace();
            var source = _workspace.CreateDirectory("source");
            var output = _workspace.CreateDirectory("output");
            var paths = new List<string>(FileCount);
            var random = new System.Random(42);

            for (var index = 0; index < FileCount; index++)
            {
                var directory = Path.Combine(source, $"dir-{index % 10:D2}");
                Directory.CreateDirectory(directory);

                var filePath = Path.Combine(directory, $"file-{index:D3}.bin");
                var data = new byte[FileSizeBytes];
                random.NextBytes(data);
                File.WriteAllBytes(filePath, data);
                paths.Add(filePath);
            }

            _paths = paths;
            _archiver = new ZipArchiver(
                output,
                source,
                "benchmark.zip",
                maxWorkers: Workers,
                overwrite: true);
        }

        [Benchmark]
        public async Task<string> Archive()
        {
            return await _archiver.StartAsync(_paths);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _workspace.Dispose();
        }
    }
}
