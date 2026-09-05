namespace Modulus.Messaging.Integration.Tests;

using System.Text;
using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.EventBus.RabbitMQ.Extensions;
using Modulus.Events.Abstractions;
using Modulus.Events.Extensions;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;

/// <summary>
/// One RabbitMQ broker container shared across the tests in this class —
/// each test uses its own uniquely-named exchange/queue so tests don't
/// interfere with each other on the shared broker.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    public const string UserName = "guest";
    public const string Password = "guest";

    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithUsername(UserName)
        .WithPassword(Password)
        .Build();

    public string HostName => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(5672);

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
/// Exercises the RabbitMQ <see cref="Modulus.Events.Abstractions.IModuleBus"/>
/// end-to-end against a real broker (Testcontainers): publish → broker →
/// <c>RabbitMqEventConsumer</c> (a hosted <see cref="BackgroundService"/>) →
/// <c>IntegrationEventDispatcher</c> → handler. Was previously untested —
/// this project had zero source files.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RabbitMqEventBusTests : IClassFixture<RabbitMqFixture>, IAsyncDisposable
{
    private readonly RabbitMqFixture _fixture;
    private ServiceProvider? _provider;
    private readonly List<IHostedService> _hostedServices = [];

    public RabbitMqEventBusTests(RabbitMqFixture fixture) => _fixture = fixture;

    private async Task<ServiceProvider> StartAsync(
        string exchange, string queue, string? deadLetterExchange = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // AddRabbitMqEventBus binds RabbitMqOptions from IConfiguration (in
        // addition to the inline configure callback below), so it must be
        // registered even though every value the tests need comes from the
        // callback.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddModulusEvents(typeof(RabbitMqEventBusTests).Assembly);
        services.AddRabbitMqEventBus(opts =>
        {
            opts.HostName = _fixture.HostName;
            opts.Port = _fixture.Port;
            opts.UserName = RabbitMqFixture.UserName;
            opts.Password = RabbitMqFixture.Password;
            opts.ExchangeName = exchange;
            opts.QueueName = queue;
            opts.DeadLetterExchange = deadLetterExchange;
            // Keep the suite fast: don't wait the full default 5s before a
            // reconnect attempt on a deliberately-crashed consumer.
            opts.ReconnectDelayMs = 500;
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
            await Task.Delay(100);
        }
        condition().Should().BeTrue("condition did not become true within the timeout");
    }

    [Fact]
    public async Task PublishAsync_HandlerReceivesEvent_ExactlyOnce()
    {
        var exchange = "test.ex." + Guid.NewGuid().ToString("N");
        var queue = "test.q." + Guid.NewGuid().ToString("N");
        TestHandler.Received.Clear();

        var provider = await StartAsync(exchange, queue);
        // Give the consumer's background loop a moment to declare the
        // exchange/queue and start consuming before we publish.
        await Task.Delay(1000);

        var bus = provider.GetRequiredService<IModuleBus>();
        var @event = new TestEvent();
        await bus.PublishAsync(@event);

        await WaitUntilAsync(
            () => TestHandler.Received.ContainsKey(@event.EventId),
            TimeSpan.FromSeconds(15));

        TestHandler.Received[@event.EventId].Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_HandlerThrows_MessageDeadLettered_NotRetried()
    {
        // Documents the CURRENT behaviour (an H-finding, tracked for a future
        // fix): the RabbitMQ consumer nacks requeue:false on the FIRST
        // exception — no bounded retry with backoff like the Kafka consumer
        // or the outbox. A handler failure goes straight to the DLX.
        var exchange = "test.ex." + Guid.NewGuid().ToString("N");
        var queue = "test.q." + Guid.NewGuid().ToString("N");
        var dlx = "test.dlx." + Guid.NewGuid().ToString("N");
        var dlQueue = "test.dlq." + Guid.NewGuid().ToString("N");
        ThrowingHandler.CallCount = 0;

        // The consumer declares the MAIN exchange/queue but never declares
        // the dead-letter exchange itself (it only points the queue at it via
        // x-dead-letter-exchange) — a queue bound to it must exist beforehand
        // or the broker silently drops dead-lettered messages.
        await DeclareDeadLetterTopologyAsync(dlx, dlQueue);

        var provider = await StartAsync(exchange, queue, dlx);
        await Task.Delay(1000);

        var bus = provider.GetRequiredService<IModuleBus>();
        var @event = new ThrowingTestEvent();
        await bus.PublishAsync(@event);

        // Consumed once from the main queue (and throws) — never redelivered
        // there, because the nack does not requeue.
        await WaitUntilAsync(() => ThrowingHandler.CallCount >= 1, TimeSpan.FromSeconds(15));

        var deadLettered = await TryConsumeOneAsync(dlQueue, TimeSpan.FromSeconds(10));
        deadLettered.Should().NotBeNull("the failed message must land on the dead-letter queue");
        deadLettered!.Should().Contain(@event.EventId.ToString());

        // Give any (absent) retry a chance to prove it really doesn't happen.
        await Task.Delay(2000);
        ThrowingHandler.CallCount.Should().Be(1,
            "no bounded retry exists yet on this path — first failure dead-letters immediately");
    }

    private async Task DeclareDeadLetterTopologyAsync(string dlx, string dlQueue)
    {
        var factory = new ConnectionFactory
        {
            HostName = _fixture.HostName,
            Port = _fixture.Port,
            UserName = RabbitMqFixture.UserName,
            Password = RabbitMqFixture.Password,
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(dlx, ExchangeType.Fanout, durable: true);
        await channel.QueueDeclareAsync(dlQueue, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(dlQueue, dlx, routingKey: string.Empty);
    }

    private async Task<string?> TryConsumeOneAsync(string queue, TimeSpan timeout)
    {
        var factory = new ConnectionFactory
        {
            HostName = _fixture.HostName,
            Port = _fixture.Port,
            UserName = RabbitMqFixture.UserName,
            Password = RabbitMqFixture.Password,
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = await channel.BasicGetAsync(queue, autoAck: true);
            if (result is not null)
                return Encoding.UTF8.GetString(result.Body.Span);
            await Task.Delay(200);
        }
        return null;
    }

    // ── Test doubles ─────────────────────────────────────────────
    // [IntegrationEventName] is required here, not cosmetic: without it,
    // IntegrationEventNaming falls back to Type.FullName, which for a nested
    // class contains '+' — an invalid Kafka topic character. AddModulusEvents
    // scans the whole assembly, so an unattributed event here leaks a
    // malformed topic name into KafkaEventBusTests' consumer subscription
    // list in the same test run and silently breaks its broker connectivity
    // (confirmed via KafkaBackgroundServiceSanityTests).
    [IntegrationEventName("rabbitmq-test.event.v1")]
    public sealed class TestEvent : IIntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public string EventType { get; init; } = "rabbitmq-test.event.v1";
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

    [IntegrationEventName("rabbitmq-test.throwing-event.v1")]
    public sealed class ThrowingTestEvent : IIntegrationEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public string EventType { get; init; } = "rabbitmq-test.throwing-event.v1";
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    }

    public sealed class ThrowingHandler : IIntegrationEventHandler<ThrowingTestEvent>
    {
        public static int CallCount;

        public Task HandleAsync(ThrowingTestEvent @event, CancellationToken ct)
        {
            Interlocked.Increment(ref CallCount);
            throw new InvalidOperationException("simulated handler failure");
        }
    }
}
