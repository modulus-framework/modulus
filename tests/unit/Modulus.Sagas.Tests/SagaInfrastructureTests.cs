using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Events;
using Modulus.Events.Abstractions;
using Modulus.Sagas.Extensions;
using Xunit;

namespace Modulus.Sagas.Tests;

[Trait("Category", "Unit")]
public sealed class SagaInfrastructureTests
{
    // ── SagaConfigurationBuilder ──────────────────────────────────

    [Fact]
    public void Builder_Defaults_AreNoRebusOverrideNoPolly()
    {
        var builder = new SagaConfigurationBuilder();

        builder.HandlerAssemblies.Should().BeEmpty();
        builder.PollyOptions.Should().BeNull();
        builder.ShouldReplaceModuleBus.Should().BeFalse();
        builder.ShouldReplaceOutboxDispatcher.Should().BeFalse();

        // A passthrough Rebus configurer is always available
        FluentActions.Invoking(() => builder.RebusConfigurer)
            .Should().NotThrow();
        builder.RebusConfigurer.Should().NotBeNull();
    }

    [Fact]
    public void Builder_FluentMethods_SetExpectedState()
    {
        var builder = new SagaConfigurationBuilder()
            .HandlersFromAssemblyOf<SagaInfrastructureTests>()
            .ReplaceModuleBus()
            .ReplaceOutboxDispatcher()
            .PollyRetry(o => o.MaxRetryAttempts = 7);

        builder.HandlerAssemblies.Should().Contain(typeof(SagaInfrastructureTests).Assembly);
        builder.ShouldReplaceModuleBus.Should().BeTrue();
        builder.ShouldReplaceOutboxDispatcher.Should().BeTrue();
        builder.PollyOptions!.MaxRetryAttempts.Should().Be(7);
    }

    [Fact]
    public void AddModulusSagas_RegistersAdapters_ForIntegrationEventHandlers()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        services.AddModulusSagas(b => b.HandlersFromAssemblyOf<SagaInfrastructureTests>());

        services.Should().Contain(d =>
            d.ServiceType == typeof(Rebus.Handlers.IHandleMessages<TestSagaEvent>)
            && d.ImplementationType == typeof(Modulus.Sagas.Bus.IntegrationEventHandlerAdapter<TestSagaEvent>));
    }

    // ── Polly pipeline factory ────────────────────────────────────

    [Fact]
    public async Task PollyPipeline_ZeroRetries_ThrowsImmediately()
    {
        var pipeline = Resilience.PollyPipelineFactory.Create(
            new PollyRetryOptions { MaxRetryAttempts = 0 });

        var attempts = 0;
        var act = async () => await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            throw new InvalidOperationException("boom");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task PollyPipeline_RetriesTransientFailures_UpToLimit()
    {
        var pipeline = Resilience.PollyPipelineFactory.Create(
            new PollyRetryOptions { MaxRetryAttempts = 2, BaseDelay = TimeSpan.Zero, UseJitter = false });

        var attempts = 0;
        var act = async () => await pipeline.ExecuteAsync<object?>(_ =>
        {
            attempts++;
            throw new InvalidOperationException("boom");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        attempts.Should().Be(3, "1 initial attempt + 2 retries");
    }

    // ── Test doubles ───────────────────────────────────────────────

    /// <summary>
    /// A fake integration-event handler in this assembly — its mere existence
    /// must cause AddModulusSagas to register a matching Rebus adapter.
    /// </summary>
    internal sealed class HandlerAdapterProbe : IIntegrationEventHandler<TestSagaEvent>
    {
        public Task HandleAsync(TestSagaEvent @event, CancellationToken ct)
            => Task.CompletedTask;
    }

    public sealed record TestSagaEvent(Guid OrderId)
        : Modulus.Core.Abstractions.Domain.DomainEventBase, IIntegrationEvent
    {
        public string EventType => "test.saga-event.v1";
    }
}
