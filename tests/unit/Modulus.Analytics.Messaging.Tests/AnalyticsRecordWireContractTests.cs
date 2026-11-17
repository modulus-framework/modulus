namespace Modulus.Analytics.Messaging.Tests;

using System.Text.Json;
using FluentAssertions;
using Modulus.Events;
using Modulus.Events.Abstractions;
using Xunit;

/// <summary>
/// Pins the wire format that the Flink job in deploy/flink/analytics-records.sql reads: the topic it subscribes to, the envelope
/// fields it selects, and the payload fields that JSON_VALUE extracts. If one of these changes, this test fails before the job
/// silently reads nothing.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AnalyticsRecordWireContractTests
{
    private const string DefaultTopicPrefix = "modulus";

    [Fact]
    public void The_Flink_topic_is_the_default_topic_prefix_plus_the_event_wire_name()
    {
        var topic = $"{DefaultTopicPrefix}.{IntegrationEventNaming.GetName(typeof(AnalyticsRecordPublished))}";

        topic.Should().Be("modulus.analytics-messaging.analytics-record-published.v1");
        ReadFlinkSql().Should().Contain($"'topic' = '{topic}'");
    }

    [Fact]
    public void The_envelope_carries_the_fields_the_Flink_job_selects()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var tenant = Guid.NewGuid();
        var @event = new AnalyticsRecordPublished("orders", 2, tenant, 7, "{\"total\":12.5}")
        {
            EventId = Guid.NewGuid(),
            OccurredAt = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc),
        };
        var envelope = new IntegrationEventEnvelope
        {
            EventId = @event.EventId,
            OccurredAt = @event.OccurredAt,
            TypeName = IntegrationEventNaming.GetName(typeof(AnalyticsRecordPublished)),
            RoutingKey = IntegrationEventNaming.GetName(typeof(AnalyticsRecordPublished)),
            Payload = serializer.Serialize(@event, typeof(AnalyticsRecordPublished)),
            TenantId = tenant,
        };

        using var document = JsonDocument.Parse(serializer.Serialize(envelope, typeof(IntegrationEventEnvelope)));
        var root = document.RootElement;

        root.TryGetProperty("eventId", out _).Should().BeTrue();
        root.TryGetProperty("typeName", out var typeName).Should().BeTrue();
        typeName.GetString().Should().Be("analytics-messaging.analytics-record-published.v1");
        root.TryGetProperty("payload", out var payload).Should().BeTrue();
        payload.ValueKind.Should().Be(JsonValueKind.String, "the payload is the event as a JSON string, read with JSON_VALUE");
        root.TryGetProperty("tenantId", out _).Should().BeTrue();
        // correlationId is omitted when null; the Flink table declares it nullable, so a missing field reads as NULL.
    }

    [Fact]
    public void The_payload_carries_the_fields_the_Flink_job_extracts()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        var @event = new AnalyticsRecordPublished("orders", 2, Guid.NewGuid(), 7, "{\"total\":12.5}");

        using var document = JsonDocument.Parse(serializer.Serialize(@event, typeof(AnalyticsRecordPublished)));
        var root = document.RootElement;

        foreach (var field in new[] { "tenantId", "dataset", "schemaVersion", "revision", "valuesJson" })
        {
            root.TryGetProperty(field, out _).Should().BeTrue($"the Flink job reads '$.{field}'");
        }
    }

    private static string ReadFlinkSql()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "modulus.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test runs inside the repository");
        return File.ReadAllText(Path.Combine(directory!.FullName, "deploy", "flink", "analytics-records.sql"));
    }
}
