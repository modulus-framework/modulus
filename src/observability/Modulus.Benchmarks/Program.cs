using BenchmarkDotNet.Running;

namespace Modulus.Benchmarks;

internal static class Program
{
    private static void Main(string[] args)
    {
        BenchmarkRunner.Run<ModuleLoaderBenchmarks>();
        BenchmarkRunner.Run<DomainEventBenchmarks>();
    }
}
