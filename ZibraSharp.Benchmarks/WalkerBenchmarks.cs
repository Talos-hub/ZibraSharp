using System.IO;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using ZibraSharp.Internal.Config;
using ZibraSharp.Internal.Walkers;

namespace ZibraSharp.Benchmarks
{
    /// <summary>
    /// Measures recursive directory scanning.
    /// </summary>
    [MemoryDiagnoser]
    public class WalkerBenchmarks
    {
        private BenchmarkWorkspace _workspace = null!;
        private Walker _walker = null!;

        [GlobalSetup]
        public void Setup()
        {
            _workspace = new BenchmarkWorkspace();
            var source = _workspace.CreateDirectory("source");

            for (var directoryIndex = 0; directoryIndex < 20; directoryIndex++)
            {
                var directory = Path.Combine(source, $"dir-{directoryIndex:D2}");
                Directory.CreateDirectory(directory);

                for (var fileIndex = 0; fileIndex < 50; fileIndex++)
                {
                    File.WriteAllText(
                        Path.Combine(directory, $"file-{fileIndex:D2}.txt"),
                        "benchmark");
                }
            }

            _walker = new Walker(source, new JsonConfig(_workspace.Path));
        }

        [Benchmark]
        public async Task<int> Walk()
        {
            var paths = await _walker.WalkAsync();
            return paths.Count;
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _workspace.Dispose();
        }
    }
}
