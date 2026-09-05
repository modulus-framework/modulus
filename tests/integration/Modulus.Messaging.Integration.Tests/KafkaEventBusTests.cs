namespace Modulus.Messaging.Integration.Tests;

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.EventBus.Kafka.Extensions;
using Modulus.Events.Abstractions;
using Modulus.Events.Extensions;
using Testcontainers.Kafka;
using Xunit;

/// <summary>
/// One Kafka broker container shared across the tests in this class — each
/// test uses its own uniquely-named topic prefix/consumer group so tests
/// don't interfere with each other on the shared broker.
/// </summary>
public sealed class KafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer _container =
        new KafkaBuilder("apache/kafka:3.8.0").Build();

    public string BootstrapServers => _container.GetBootstrapAddress();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
/// Exercises the Kafka <see cref="Modulus.Events.Abstractions.IModuleBus"/>
/// end-to-end against a real broker (Testcontainers): publish → broker →
/// <c>KafkaEventConsumer</c> (a hosted <see cref="BackgroundService"/>) →
/// <c>IntegrationEventDispatcher</c> → handler, including the seek-based
/// bounded-retry-then-park behaviour that distinguishes it from the RabbitMQ
/// consumer (which dead-letters on the first failure — see
/// <see cref="RabbitMqEventBusTests"/>). Was previously untested — this
/// project had zero source files.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KafkaEventBusTests : IClassFixture<KafkaFixture>, IAsyncDisposable
{
    private readonly KafkaFixture _fixture;
    private ServiceProvider? _provider;
    private readonly List<IHostedService> _hostedServices = [];

    public KafkaEventBusTests(KafkaFixture fixture) => _fixture = fixture;

    private async Task<ServiceProvider> StartAsync(
        string topicPrefix, string groupId,
        int maxDeliveryAttempts = 5, int redeliveryMaxBackoffMs = 200)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // DefaultPartitionKeyProvider takes ICurrentTenant — required by DI's
        // constructor resolution regardless of the parameter's nullable
        // annotation (that's compile-time only), so it must be registered.
        services.AddSingleton<ICurrentTenant, NullCurrentTenant>();
        services.AddModulusEvents(typeof(KafkaEventBusTests).Assembly);
        services.AddKafkaEventBus(opts =>
        {
            opts.BootstrapServers = _fixture.BootstrapServers;
            opts.TopicPrefix = topicPrefix;
            opts.GroupId = groupId;
            opts.AutoOffsetReset = "earliest";
            opts.MaxDeliveryAttempts = maxDeliveryAttempts;
            opts.RedeliveryMaxBackoffMs = redeliveryMaxBackoffMs;
        });

        _provider = services.BuildServiceProvider();
        _hostedServices.AddRange(_provider.GetServices<IHostedService>());
        foreach (var hosted in _hostedServices)
            await hosted.StartAsync(default);

        return _provider;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var hosted in _hostedServices)
            await hosted.StopAsync(default);
        if (_provider is not null)
            await _provider.DisposeAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(200);
        }
        condition().Should().BeTrue("condition did not become true within the timeout");
    }

    [Fact]
    public async Task PublishAsync_HandlerReceivesEvent_ExactlyOnce()
    {
        var prefix = "test" + Guid.NewGuid().ToString("N");
        var group = "group-" + Guid.NewGuid().ToString("N");
        TestHandler.Received.Clear();

        var provider = await StartAsync(prefix, group);
        // Give the consumer time to subscribe and join the group before
        // publishing — a message produced before the subscription settles
        // can be missed depending on offset reset timing.
        await Task.Delay(3000);

        var bus = provider.GetRequiredService<IModuleBus>();
        var @event = new TestEvent();
        await bus.PublishAsync(@event);

        await WaitUntilAsync(
            () => TestHandler.Received.ContainsKey(@event.EventId),
            TimeSpan.FromSeconds(30));

        TestHandler.Received[@event.EventId].Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_HandlerFailsThenSucceeds_RedeliveredViaSeek_NoDataLoss()
    {
        var prefix = "test" + Guid.NewGuid().ToString("N");
        var group = "group-" + Guid.NewGuid().ToString("N");
        FlakyHandler.CallCount = 0;
        FlakyHandler.FailuresBeforeSuccess = 2;
        FlakyHandler.Succeeded.Clear();

        var provider = await StartAsync(prefix, group, maxDeliveryAttempts: 5);
        await Task.Delay(3000);

        var bus = provider.GetRequiredService<IModuleBus>();
        var @event = new FlakyEvent();
        await bus.PublishAsync(@event);

        // Two failed deliveries (seek-and-retry) then a third that succeeds —
        // must eventually dispatch, proving the seek-based redelivery works.
        await WaitUntilAsync(
            () => FlakyHandler.Succeeded.ContainsKey(@event.EventId),
            TimeSpan.FromSeconds(30));

        FlakyHandler.CallCount.Should().Be(3,
            "two failures (seek+retry) then one success");
    }

    [Fact]
    public async Task PublishAsync_HandlerAlwaysFails_ParkedAfterMaxAttempts_PartitionNotBlocked()
    {
        var prefix = "test" + Guid.NewGuid().ToString("N");
        var group = "group-" + Guid.NewGuid().ToString("N");
        const int maxAttempts = 3;
        AlwaysFailingHandler.CallCounts.Clear();
        AlwaysFailingHandler.PoisonEventId = null;

        var provider = await StartAsync(prefix, group, maxDeliveryAttempts: maxAttempts, redeliveryMaxBackoffMs: 100);
        await Task.Delay(3000);

        var bus = provider.GetRequiredService<IModuleBus>();

        // Same event TYPE (so same topic/partition) as the follow-up message
        // below — the handler only ever fails for THIS specific EventId, so
        // a passing follow-up genuinely proves the partition isn't stuck
        // behind the poisoned one, rather than just using an unrelated topic.
        var poison = new AlwaysFailingEvent();
        AlwaysFailingHandler.PoisonEventId = poison.EventId;
        await bus.PublishAsync(poison);

        // Retries up to maxAttempts, then commits past it (parks) rather than
        // blocking the partition forever.
        await WaitUntilAsync(
            () => AlwaysFailingHandler.CallCounts.GetValueOrDefault(poison.EventId) >= maxAttempts,
            TimeSpan.FromSeconds(30));

        // Give it a moment to make sure it stops retrying past maxAttempts.
        await Task.Delay(1000);
        AlwaysFailingHandler.CallCounts[poison.EventId].Should().Be(maxAttempts,
            "the poison message must be parked, not retried indefinitely");

        // A second message of the SAME type/topic/partition, but one the
        // handler lets through — proves the partition is not stuck behind
        // the parked poison message.
        var followUp = new AlwaysFailingEvent();
        await bus.PublishAsync(followUp);
        await WaitUntilAsync(
            () => AlwaysFailingHandler.CallCounts.ContainsKey(followUp.EventId),
            TimeSpan.FromSeconds(30));
        AlwaysFailingHandler.CallCounts[followUp.EventId].Should().Be(1);
    }

    // ── Test doubles ─────────────────────────────────────────────
    [IntegrationEventName("kafka-test.event.v1")]
    public sealed class TestEvent : IIntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public string EventType { get; init; } = "kafka-test.event.v1";
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    public sealed class TestHandler : IIntegrationEventHandler<TestEvent>
    {
        public static readonly ConcurrentDictionary<Guid, int> Received = new();

        public Task HandleAsync(TestEvent @event, CancellationToken ct)
        {
            Received.AddOrUpdate(@event.EventId, 1, (_, count) => count + 1);
            return Task.CompletedTask;
        }
    }

    [IntegrationEventName("kafka-test.flaky-event.v1")]
    public sealed class FlakyEvent : IIntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public string EventType { get; init; } = "kafka-test.flaky-event.v1";
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    public sealed class FlakyHandler : IIntegrationEventHandler<FlakyEvent>
    {
        public static int CallCount;
        public static int FailuresBeforeSuccess;
        public static readonly ConcurrentDictionary<Guid, byte> Succeeded = new();

        public Task HandleAsync(FlakyEvent @event, CancellationToken ct)
        {
            var attempt = Interlocked.Increment(ref CallCount);
            if (attempt <= FailuresBeforeSuccess)
                throw new InvalidOperationException($"simulated failure #{attempt}");

            Succeeded[@event.EventId] = 1;
            return Task.CompletedTask;
        }
    }

    [IntegrationEventName("kafka-test.always-failing-event.v1")]
    public sealed class AlwaysFailingEvent : IIntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public string EventType { get; init; } = "kafka-test.always-failing-event.v1";
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    public sealed class AlwaysFailingHandler : IIntegrationEventHandler<AlwaysFailingEvent>
    {
        public static readonly ConcurrentDictionary<Guid, int> CallCounts = new();

        /// <summary>Only THIS event id fails — every other instance succeeds
        /// immediately, so a follow-up message proves the partition isn't
        /// stuck behind a parked poison message of the same type/topic.</summary>
        public static Guid? PoisonEventId;

        public Task HandleAsync(AlwaysFailingEvent @event, CancellationToken ct)
        {
            CallCounts.AddOrUpdate(@event.EventId, 1, (_, count) => count + 1);
            if (@event.EventId == PoisonEventId)
                throw new InvalidOperationException("simulated permanent failure");
            return Task.CompletedTask;
        }
    }
}
