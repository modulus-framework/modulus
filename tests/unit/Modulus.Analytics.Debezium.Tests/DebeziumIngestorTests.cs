namespace Modulus.Analytics.Debezium.Tests;

using FluentAssertions;
using Xunit;

public sealed class DebeziumIngestorTests
{
    private sealed class CapturingQueue : IAnalyticsQueue
    {
        public List<AnalyticsRecord> Enqueued { get; } = [];

        public Task EnqueueAsync(IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken)
        {
            Enqueued.AddRange(records);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task The_readable_changes_are_queued_and_the_unreadable_ones_are_reported()
    {
        var tenant = Guid.NewGuid();
        var dataset = new AnalyticsDatasetDescriptor("orders_cdc", 1, "sales", [new("id", AnalyticsFieldType.Int64), new("tenant_id", AnalyticsFieldType.Guid)]);
        var reader = new DebeziumChangeReader([new DebeziumTableMapping("orders", dataset, KeyColumn: "id")]);
        var queue = new CapturingQueue();
        var ingestor = new DebeziumIngestor(reader, queue);
        var good = "{\"before\":null,\"after\":{\"id\":1,\"tenant_id\":\"" + tenant.ToString("D") + "\"},\"source\":{\"table\":\"orders\",\"ts_ms\":5,\"lsn\":10},\"op\":\"c\",\"ts_ms\":5}";

        var result = await ingestor.IngestAsync(["broken", good], CancellationToken.None);

        queue.Enqueued.Should().ContainSingle().Which.TenantId.Should().Be(tenant);
        result.Problems.Should().ContainSingle().Which.Should().Contain("not JSON");
    }

    [Fact]
    public async Task A_batch_with_nothing_readable_queues_nothing()
    {
        var dataset = new AnalyticsDatasetDescriptor("orders_cdc", 1, "sales", [new("id", AnalyticsFieldType.Int64), new("tenant_id", AnalyticsFieldType.Guid)]);
        var queue = new CapturingQueue();
        var ingestor = new DebeziumIngestor(new DebeziumChangeReader([new DebeziumTableMapping("orders", dataset, KeyColumn: "id")]), queue);

        await ingestor.IngestAsync(["{\"op\":\"t\",\"source\":{\"table\":\"orders\"}}"], CancellationToken.None);

        queue.Enqueued.Should().BeEmpty();
    }
}
