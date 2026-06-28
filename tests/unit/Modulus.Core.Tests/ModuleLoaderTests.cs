using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;
using FluentAssertions;

namespace Modulus.Core.Tests;

[Trait("Category", "Unit")]
public sealed class ModuleLoaderTests
{
    private readonly ModuleLoader _loader = new();

    [Fact]
    public void BuildGraph_NoDependencies_ReturnsSingleDescriptor()
    {
        var modules = new IModule[] { new AlphaModule() };
        var graph = _loader.BuildGraph(modules);
        graph.Should().HaveCount(1);
        graph[0].Name.Should().Be(nameof(AlphaModule));
        graph[0].InitOrder.Should().Be(0);
    }

    [Fact]
    public void BuildGraph_WithDependency_OrdersCorrectly()
    {
        // BetaModule depends on AlphaModule
        var modules = new IModule[] { new BetaModule(), new AlphaModule() };
        var graph = _loader.BuildGraph(modules);
        var alphaIdx = graph.ToList().FindIndex(d => d.ModuleType == typeof(AlphaModule));
        var betaIdx = graph.ToList().FindIndex(d => d.ModuleType == typeof(BetaModule));
        alphaIdx.Should().BeLessThan(betaIdx);
    }

    [Fact]
    public void BuildGraph_CircularDependency_Throws()
    {
        var modules = new IModule[] { new CyclicA(), new CyclicB() };
        var act = () => _loader.BuildGraph(modules);
        act.Should().Throw<CircularDependencyException>();
    }

    [Fact]
    public void BuildGraph_MissingDependency_Throws()
    {
        var modules = new IModule[] { new BetaModule() }; // AlphaModule missing
        var act = () => _loader.BuildGraph(modules);
        act.Should().Throw<ModuleNotFoundException>();
    }

    // ── Test helpers ─────────────────────────────────────────────
    private class AlphaModule : IModule
    {
        public Type[] DependsOn => [];
        public void ConfigureServices(IServiceCollection s, IConfiguration c) { }
        public Task InitializeAsync(ModuleContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private class BetaModule : IModule
    {
        public Type[] DependsOn => [typeof(AlphaModule)];
        public void ConfigureServices(IServiceCollection s, IConfiguration c) { }
        public Task InitializeAsync(ModuleContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private class CyclicA : IModule
    {
        public Type[] DependsOn => [typeof(CyclicB)];
        public void ConfigureServices(IServiceCollection s, IConfiguration c) { }
        public Task InitializeAsync(ModuleContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private class CyclicB : IModule
    {
        public Type[] DependsOn => [typeof(CyclicA)];
        public void ConfigureServices(IServiceCollection s, IConfiguration c) { }
        public Task InitializeAsync(ModuleContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;
    }
}