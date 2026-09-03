using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Core;
using Modulus.Core.Abstractions;
using Xunit;
using FluentAssertions;

namespace Modulus.Core.Tests;

/// <summary>
/// Spec for the module loader: descriptors follow registration order,
/// initialization runs in that order, shutdown runs in reverse, and duplicate
/// module types are rejected.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ModuleLoaderTests
{
    [Fact]
    public void Descriptors_FollowRegistrationOrder()
    {
        var loader = new ModuleLoader([new BetaModule(), new AlphaModule()]);

        var descriptors = loader.GetDescriptors();

        descriptors.Should().HaveCount(2);
        descriptors[0].ModuleType.Should().Be(typeof(BetaModule));
        descriptors[0].InitOrder.Should().Be(0);
        descriptors[1].ModuleType.Should().Be(typeof(AlphaModule));
        descriptors[1].InitOrder.Should().Be(1);
    }

    [Fact]
    public async Task InitializeAllAsync_RunsInRegistrationOrder()
    {
        var log = new List<string>();
        var alpha = new AlphaModule { InitLog = log };
        var beta = new BetaModule { InitLog = log };
        var loader = new ModuleLoader([alpha, beta]);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton(alpha);
        services.AddSingleton(beta);
        await using var sp = services.BuildServiceProvider();

        await loader.InitializeAllAsync(sp);

        log.Should().Equal(["AlphaModule", "BetaModule"]);
    }

    [Fact]
    public async Task ShutdownAllAsync_RunsInReverseRegistrationOrder()
    {
        var log = new List<string>();
        var alpha = new AlphaModule { ShutdownLog = log };
        var beta = new BetaModule { ShutdownLog = log };
        var loader = new ModuleLoader([alpha, beta]);
        await using var sp = BuildLoggingProvider();

        await loader.ShutdownAllAsync(sp);

        log.Should().Equal(["BetaModule", "AlphaModule"]);
    }

    [Fact]
    public async Task ShutdownAllAsync_OneModuleThrows_RemainingModulesStillShutDown()
    {
        // H8: a module whose ShutdownAsync throws must not abort the loop —
        // every module still queued (earlier in registration order, later in
        // shutdown order) must still get its own ShutdownAsync called.
        var log = new List<string>();
        var alpha = new AlphaModule { ShutdownLog = log };
        // Beta shuts down FIRST (reverse registration order) and throws.
        var beta = new ThrowingShutdownModule { ShutdownLog = log };
        var loader = new ModuleLoader([alpha, beta]);
        await using var sp = BuildLoggingProvider();

        // Must not throw — the exception is caught and logged, not propagated.
        await loader.ShutdownAllAsync(sp);

        log.Should().Equal(["AlphaModule"],
            "alpha's ShutdownAsync must still run even though beta's threw first");
    }

    private static ServiceProvider BuildLoggingProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void DuplicateModuleType_Throws()
    {
        var act = () => new ModuleLoader([new AlphaModule(), new AlphaModule()]);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage($"*{typeof(AlphaModule).FullName}*registered more than once*");
    }

    // ── Test modules ──────────────────────────────────────────────

    private class AlphaModule : IModule
    {
        public List<string>? InitLog { get; set; }
        public List<string>? ShutdownLog { get; set; }

        public void ConfigureServices(IServiceCollection s, IConfiguration c) { }

        public Task InitializeAsync(ModuleContext ctx, CancellationToken ct)
        {
            InitLog?.Add(GetType().Name);
            return Task.CompletedTask;
        }

        public virtual Task ShutdownAsync(CancellationToken ct)
        {
            ShutdownLog?.Add(GetType().Name);
            return Task.CompletedTask;
        }
    }

    private class BetaModule : AlphaModule { }

    private sealed class ThrowingShutdownModule : AlphaModule
    {
        public override Task ShutdownAsync(CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }
}
