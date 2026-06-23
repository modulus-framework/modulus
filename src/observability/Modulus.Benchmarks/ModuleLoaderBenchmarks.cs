using BenchmarkDotNet.Attributes;
using Modulus.Core;
using Modulus.Core.Abstractions;

namespace Modulus.Benchmarks;

/// <summary>
/// Benchmarks the topological sort algorithm in ModuleLoader.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class ModuleLoaderBenchmarks
{
    private List<TestModule> _modules = null!;
    private ModuleLoader _loader = null!;

    [Params(5, 20, 50, 100)]
    public int ModuleCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _modules = [];
        for (var i = 0; i < ModuleCount; i++)
            _modules.Add(new TestModule($"Module{i}"));

        // Wire dependencies: each module depends on the previous one
        for (var i = 1; i < _modules.Count; i++)
            _modules[i].SetDependsOn(_modules[i - 1].GetType());

        _loader = new ModuleLoader();
    }

    [Benchmark]
    public IReadOnlyList<ModuleDescriptor> BuildGraph()
        => _loader.BuildGraph(_modules);

    private class TestModule(string name) : IModule
    {
        private Type[] _deps = [];

        public string Name { get; } = name;
        public Type[] DependsOn => _deps;

        public void SetDependsOn(params Type[] deps) => _deps = deps;

        public Task InitializeAsync(ModuleContext context, CancellationToken ct = default)
            => Task.CompletedTask;

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration) { }

        public Task ShutdownAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
