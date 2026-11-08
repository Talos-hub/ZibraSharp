using System.IO;
using BenchmarkDotNet.Attributes;
using ZibraSharp.Internal.Logger;

namespace ZibraSharp.Benchmarks
{
    /// <summary>
    /// Measures structured JSON logging throughput (rotation disabled).
    /// </summary>
    [MemoryDiagnoser]
    public class JsonLoggerBenchmarks
    {
        private BenchmarkWorkspace _workspace = null!;
        private JsonLogger _logger = null!;

        [GlobalSetup]
        public void Setup()
        {
            _workspace = new BenchmarkWorkspace();
            _logger = new JsonLogger(
                Path.Combine(_workspace.Path, "zibra.log"),
                LogLevel.Debug,
                maxFileSizeBytes: 0);
        }

        [Benchmark]
        public void WriteOneHundredEvents()
        {
            for (var index = 0; index < 100; index++)
            {
                _logger.Info("event", "Index", index, "Payload", "benchmark");
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _logger.Dispose();
            _workspace.Dispose();
        }
    }
}
