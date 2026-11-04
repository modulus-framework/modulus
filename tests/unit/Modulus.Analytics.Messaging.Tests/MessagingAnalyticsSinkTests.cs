namespace Modulus.Analytics.Messaging.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Events.Abstractions;
using Modulus.Testing.Fakes;
using Xunit;

[Trait("Category", "Unit")]
public sealed class MessagingAnalyticsSinkTests
{
    private static readonly AnalyticsDatasetDescriptor s_dataset = new("orders", 2, "commerce",
        [new("total", AnalyticsFieldType.Decimal), new("note", AnalyticsFieldType.String, true)]);

    [Fact]
    public async Task Each_record_is_published_once_with_its_id_tenant_revision_and_values()
    {
        var bus = new RecordingModuleBus();
        var sink = new MessagingAnalyticsSink(bus);
        var tenant = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
        var records = new[]
        {
            new AnalyticsRecord(Guid.NewGuid(), "orders", 2, tenant, at, new Dictionary<string, object?> { ["total"] = 12.5m, ["note"] = null }, 7),
            new AnalyticsRecord(Guid.NewGuid(), "orders", 2, tenant, at, new Dictionary<string, object?> { ["total"] = 3m, ["note"] = "gift" }, 0),
        };

        await sink.WriteAsync(s_dataset, records, default);

        var published = bus.PublishedEvents<AnalyticsRecordPublished>();
        published.Should().HaveCount(2);
        published[0].EventId.Should().Be(records[0].EventId);
        published[0].OccurredAt.Should().Be(at.UtcDateTime);
        published[0].Dataset.Should().Be("orders");
        published[0].SchemaVersion.Should().Be(2);
        published[0].TenantId.Should().Be(tenant);
        published[0].Revision.Should().Be(7);
        published[0].ValuesJson.Should().Contain("\"total\":12.5").And.Contain("\"note\":null");
        published[1].ValuesJson.Should().Contain("\"note\":\"gift\"");
    }

    [Fact]
    public void The_event_is_named_from_its_declared_module_and_version()
        => IntegrationEventNaming.GetName(typeof(AnalyticsRecordPublished)).Should().Be("analytics-messaging.analytics-record-published.v1");

    [Fact]
    public async Task An_empty_batch_publishes_nothing()
    {
        var bus = new RecordingModuleBus();
        await new MessagingAnalyticsSink(bus).WriteAsync(s_dataset, [], default);

        bus.AllPublishedEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task A_bus_failure_is_reported_as_transient()
    {
        var sink = new MessagingAnalyticsSink(new FailingBus());
        var records = new[] { new AnalyticsRecord(Guid.NewGuid(), "orders", 2, Guid.NewGuid(), DateTimeOffset.UtcNow, new Dictionary<string, object?>()) };

        var act = () => sink.WriteAsync(s_dataset, records, default);

        var error = (await act.Should().ThrowAsync<AnalyticsSinkException>()).Which;
        error.IsTransient.Should().BeTrue();
        error.InnerException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Cancellation_is_not_wrapped()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var records = new[] { new AnalyticsRecord(Guid.NewGuid(), "orders", 2, Guid.NewGuid(), DateTimeOffset.UtcNow, new Dictionary<string, object?>()) };

        var act = () => new MessagingAnalyticsSink(new RecordingModuleBus()).WriteAsync(s_dataset, records, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void AddMessaging_registers_the_sink_under_its_name()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IModuleBus>(new RecordingModuleBus());
        services.AddModulusAnalytics(a => a.AddMessaging());

        using var provider = services.BuildServiceProvider();
        var sinks = provider.GetServices<IAnalyticsSink>().ToList();

        sinks.Should().ContainSingle(s => s is MessagingAnalyticsSink);
        sinks.Single(s => s is MessagingAnalyticsSink).Name.Should().Be("messaging");
    }

    private sealed class FailingBus : IModuleBus
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IIntegrationEvent
            => throw new InvalidOperationException("broker unavailable");
    }
}
