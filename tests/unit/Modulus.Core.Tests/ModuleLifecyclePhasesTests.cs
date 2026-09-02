using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core;
using Modulus.Core.Abstractions;
using FluentAssertions;
using Xunit;

namespace Modulus.Core.Tests;

/// <summary>
/// Spec for the three-phase service configuration: all PreConfigureServices
/// run before any ConfigureServices, all ConfigureServices before any
/// PostConfigureServices, and each phase runs in registration order.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ModuleLifecyclePhasesTests
{
    [Fact]
    public void Complete_RunsPhases_PreThenConfigureThenPost_AcrossAllModules()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModule<BaseModule>();
        builder.AddModule<DependentModule>();
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
    public void Complete_WithinEachPhase_RunsInRegistrationOrder()
    {
        // BaseModule is registered before DependentModule, so in every phase
        // Base must configure before Dependent.
        var log = new List<string>();
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModule<BaseModule>();
        builder.AddModule<DependentModule>();
        Recorder.Log = log;

        builder.Complete();

        foreach (var phase in new[] { "Pre", "Configure", "Post" })
        {
            var order = log.Where(e => e.StartsWith(phase + ":")).ToArray();
            Array.IndexOf(order, $"{phase}:Base")
                .Should().BeLessThan(Array.IndexOf(order, $"{phase}:Dependent"),
                    $"in the {phase} phase, the module registered first runs first");
        }
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

    private sealed class DependentModule : RecordingModule { }
}
