using System.Linq;
using BenchmarkDotNet.Attributes;
using ZibraSharp.Internal.Config;

namespace ZibraSharp.Benchmarks
{
    /// <summary>
    /// Measures parsing of the space-separated extension list.
    /// </summary>
    [MemoryDiagnoser]
    public class ExtensionParsingBenchmarks
    {
        private string _input = string.Empty;

        [GlobalSetup]
        public void Setup()
        {
            _input = string.Join(
                " ",
                Enumerable.Range(0, 50).Select(index => $".ext{index:D2}"));
        }

        [Benchmark]
        public int Parse()
        {
            return JsonConfig.ParseExtensions(_input).Count;
        }
    }
}
