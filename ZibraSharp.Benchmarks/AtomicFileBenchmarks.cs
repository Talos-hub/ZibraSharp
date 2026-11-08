using System.IO;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using ZibraSharp.Internal.IO;

namespace ZibraSharp.Benchmarks
{
    /// <summary>
    /// Measures the atomic write path used by configuration and queue files.
    /// </summary>
    [MemoryDiagnoser]
    public class AtomicFileBenchmarks
    {
        private BenchmarkWorkspace _workspace = null!;
        private string _path = string.Empty;
        private byte[] _payload = null!;

        [GlobalSetup]
        public void Setup()
        {
            _workspace = new BenchmarkWorkspace();
            _path = Path.Combine(_workspace.Path, "state.json");
            _payload = Encoding.UTF8.GetBytes(new string('x', 16 * 1024));
        }

        [Benchmark]
        public async Task Write()
        {
            await AtomicFile.WriteAsync(
                _path,
                async (stream, ct) => await stream.WriteAsync(_payload, ct));
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _workspace.Dispose();
        }
    }
}
