using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core;
using Modulus.Core.Abstractions;
using FluentAssertions;
using Xunit;

namespace Modulus.Core.Tests;

[Trait("Category", "Unit")]
public sealed class ModuleLifecyclePhasesTests
{
    [Fact]
    public void Complete_RunsPhases_PreThenConfigureThenPost_AcrossAllModules()
    {
        // All PreConfigureServices must run before ANY ConfigureServices, and all
        // ConfigureServices before ANY PostConfigureServices — the whole point of
        // phasing is that a later module in ConfigureServices sees state a module
        // seeded in PreConfigureServices.
        var log = new List<string>();
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModules<DependentModule>();  // depends on BaseModule
        Recorder.Log = log;

        builder.Complete();

        var phases = log.Select(e => e.Split(':')[0]).ToArray();
        var firstConfigure = Array.IndexOf(phases, "Configure");
        var firstPost = Array.IndexOf(phases, "Post");
        var lastPre = Array.LastIndexOf(phases, "Pre");
        var lastConfigure = Array.LastIndexOf(phases, "Configure");

        lastPre.Should().BeLessThan(firstConfigure, "all Pre run before any Configure");
        lastConfigure.Should().BeLessThan(firstPost, "all Configure run before any Post");
    }

    [Fact]
    public void Complete_WithinEachPhase_RunsInDependencyOrder()
    {
        // BaseModule is a dependency of DependentModule, so in every phase Base
        // must configure before Dependent.
        var log = new List<string>();
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModules<DependentModule>();
        Recorder.Log = log;

        builder.Complete();

        foreach (var phase in new[] { "Pre", "Configure", "Post" })
        {
            var order = log.Where(e => e.StartsWith(phase + ":")).ToArray();
            Array.IndexOf(order, $"{phase}:Base")
                .Should().BeLessThan(Array.IndexOf(order, $"{phase}:Dependent"),
                    $"in the {phase} phase, the dependency runs first");
        }
    }

    [Fact]
    public void Complete_DirectIModuleImpl_WithoutOverrides_UsesNoOpDefaults()
    {
        // A module implementing IModule directly (not via ModulusModule) and not
        // overriding the new phases must still build — the default interface
        // methods supply the no-op bodies.
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModule<BareModule>();

        var act = () => builder.Complete();
        act.Should().NotThrow();
    }

    // ── Test modules ──────────────────────────────────────────────

    private static class Recorder
    {
        [ThreadStatic] public static List<string>? Log;
    }

    private class RecordingModule : ModulusModule
    {
        protected string Tag => GetType().Name.Replace("Module", "");
        public override void PreConfigureServices(IServiceCollection s, IConfiguration c)
            => Recorder.Log?.Add($"Pre:{Tag}");
        public override void ConfigureServices(IServiceCollection s, IConfiguration c)
            => Recorder.Log?.Add($"Configure:{Tag}");
        public override void PostConfigureServices(IServiceCollection s, IConfiguration c)
            => Recorder.Log?.Add($"Post:{Tag}");
    }

    private sealed class BaseModule : RecordingModule { }

    [DependsOn(typeof(BaseModule))]
    private sealed class DependentModule : RecordingModule { }

    // Implements IModule directly; overrides none of the new phase methods.
    private sealed class BareModule : IModule
    {
        public Type[] DependsOn => [];
        public void ConfigureServices(IServiceCollection s, IConfiguration c) { }
        public Task InitializeAsync(ModuleContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
