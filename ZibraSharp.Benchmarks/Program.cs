using BenchmarkDotNet.Running;
using ZibraSharp.Benchmarks;

if (args.Length == 0)
{
    // Run every benchmark suite non-interactively.
    BenchmarkRunner.Run<ZipArchiverBenchmarks>();
    BenchmarkRunner.Run<WalkerBenchmarks>();
    BenchmarkRunner.Run<ExtensionParsingBenchmarks>();
    BenchmarkRunner.Run<JsonLoggerBenchmarks>();
    BenchmarkRunner.Run<AtomicFileBenchmarks>();
}
else
{
    BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
