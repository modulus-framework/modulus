using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core;
using Modulus.Core.Abstractions;
using FluentAssertions;
using Xunit;

namespace Modulus.Core.Tests;

/// <summary>
/// Spec for explicit module registration: registration order is authoritative
/// (config phases, init, reverse shutdown), registration is idempotent, and
/// each module type is instantiated exactly once.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ModuleRegistrationTests
{
    [Fact]
    public void Complete_RunsPhases_InRegistrationOrder()
    {
        var log = new List<string>();
        Recorder.Log = log;

        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModule<FirstModule>();
        builder.AddModule<SecondModule>();
        builder.Complete();

        foreach (var phase in new[] { "Pre", "Configure", "Post" })
        {
            var order = log.Where(e => e.StartsWith(phase + ":")).ToArray();
            order.Should().Equal([$"{phase}:First", $"{phase}:Second"],
                $"in the {phase} phase, modules run in registration order");
        }
    }

    [Fact]
    public void Complete_RunsAllPrePhases_BeforeAnyConfigurePhase()
    {
        // All PreConfigureServices must run before ANY ConfigureServices, and
        // all ConfigureServices before ANY PostConfigureServices — phasing lets
        // a later module in ConfigureServices see state a module seeded in
        // PreConfigureServices.
        var log = new List<string>();
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModule<FirstModule>();
        builder.AddModule<SecondModule>();
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
    public void AddModule_SameTypeTwice_RegistersOnce()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModule<FirstModule>();
        builder.AddModule<FirstModule>();

        builder.Modules.Should().HaveCount(1);
        services.Count(s => s.ServiceType == typeof(IModule)).Should().Be(1);
    }

    [Fact]
    public void AddModule_InstantiatesEachModule_ExactlyOnce()
    {
        CountingModule.Constructions = 0;

        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModule<CountingModule>();
        builder.Complete();

        CountingModule.Constructions.Should().Be(1);
    }

    [Fact]
    public void AddModule_NonModuleType_Throws()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        var act = () => builder.AddModule(typeof(string));
        act.Should().Throw<InvalidOperationException>()
           .WithMessage($"*{typeof(string).FullName}*does not implement*{nameof(IModule)}*");
    }

    [Fact]
    public void AddModule_AbstractModuleType_Throws()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        var act = () => builder.AddModule(typeof(AbstractModuleBase));
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*abstract*");
    }

    [Fact]
    public void AddModule_RegistersModuleInDI_AsIModuleAndConcreteType()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModule<FirstModule>();

        services.Should().Contain(s => s.ServiceType == typeof(IModule));
        services.Should().Contain(s => s.ServiceType == typeof(FirstModule));
    }

    [Fact]
    public void Complete_RegistersLoaderSingleton_InRegistrationOrder()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        builder.AddModule<SecondModule>();
        builder.AddModule<FirstModule>();

        var loader = builder.Complete();

        loader.GetDescriptors().Select(d => d.ModuleType)
            .Should().Equal([typeof(SecondModule), typeof(FirstModule)]);

        // The built loader instance is registered as the IModuleLoader singleton.
        services.Should().Contain(s =>
            s.ServiceType == typeof(IModuleLoader)
            && ReferenceEquals(s.ImplementationInstance, loader));
    }

    [Fact]
    public void Complete_NoModules_StillBuildsEmptyLoader()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var builder = new ModulusBuilder(services, config);

        var loader = builder.Complete();

        loader.GetDescriptors().Should().BeEmpty();
    }

    [Fact]
    public void Complete_DirectIModuleImpl_WithoutOverrides_UsesNoOpDefaults()
    {
        // A module implementing IModule directly (not via ModulusModule) and not
        // overriding the optional phases must still build — the default
        // interface methods supply the no-op bodies.
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
        private string Tag => GetType().Name.Replace("Module", "");

        public override void PreConfigureServices(IServiceCollection s, IConfiguration c)
            => Recorder.Log?.Add($"Pre:{Tag}");
        public override void ConfigureServices(IServiceCollection s, IConfiguration c)
            => Recorder.Log?.Add($"Configure:{Tag}");
        public override void PostConfigureServices(IServiceCollection s, IConfiguration c)
            => Recorder.Log?.Add($"Post:{Tag}");
    }

    private sealed class FirstModule : RecordingModule { }

    private sealed class SecondModule : RecordingModule { }

    private sealed class CountingModule : ModulusModule
    {
        public static int Constructions;
        public CountingModule() => Interlocked.Increment(ref Constructions);
    }

    private abstract class AbstractModuleBase : ModulusModule { }

    // Implements IModule directly; overrides none of the optional phases.
    private sealed class BareModule : IModule
    {
        public void ConfigureServices(IServiceCollection s, IConfiguration c) { }
        public Task InitializeAsync(ModuleContext ctx, CancellationToken ct) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
