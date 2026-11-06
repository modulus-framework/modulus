namespace Modulus.Analytics.Debezium.Tests;

using System.Text.Json;
using FluentAssertions;
using Xunit;

public sealed class DebeziumChangeReaderTests
{
    private static readonly Guid Tenant = Guid.Parse("5e6f7a8b-9c0d-4e1f-8a2b-3c4d5e6f7a8b");

    private static readonly AnalyticsDatasetDescriptor Orders = new("orders_cdc", 1, "sales",
    [
        new("id", AnalyticsFieldType.Int64),
        new("tenant_id", AnalyticsFieldType.Guid),
        new("total", AnalyticsFieldType.Decimal),
        new("status", AnalyticsFieldType.String),
        new("placed_at", AnalyticsFieldType.Timestamp, IsNullable: true),
        new("deleted", AnalyticsFieldType.Boolean),
    ]);

    private static readonly DebeziumTableMapping OrdersMapping = new("orders", Orders, KeyColumn: "id", DeletedField: "deleted");

    private static DebeziumChangeReader Reader() => new([OrdersMapping]);

    private static string Event(string op, string? before, string? after, long lsn = 1000, string table = "orders", string schema = "public")
        => $"{{\"before\":{before ?? "null"},\"after\":{after ?? "null"},\"source\":{{\"version\":\"3.0.0.Final\",\"connector\":\"postgresql\",\"name\":\"app\",\"ts_ms\":1700000001000,\"db\":\"app\",\"schema\":\"{schema}\",\"table\":\"{table}\",\"lsn\":{lsn}}},\"op\":\"{op}\",\"ts_ms\":1700000001500}}";

    private static string Row(long id, string status = "NEW", string total = "12.50", long? placedMicros = 1700000000000000)
        => $"{{\"id\":{id},\"tenant_id\":\"{Tenant:D}\",\"total\":\"{total}\",\"status\":\"{status}\",\"placed_at\":{(placedMicros?.ToString() ?? "null")}}}";

    [Fact]
    public void An_insert_becomes_a_record_with_its_typed_values_its_source_position_and_no_delete_marker()
    {
        var result = Reader().Read([Event("c", null, Row(1))]);

        result.Problems.Should().BeEmpty();
        var record = result.Records.Should().ContainSingle().Subject;
        record.Dataset.Should().Be("orders_cdc");
        record.SchemaVersion.Should().Be(1);
        record.TenantId.Should().Be(Tenant);
        record.Revision.Should().Be(1000, "the source's log position orders the changes");
        record.OccurredAt.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000001000));
        record.Values["id"].Should().Be(1L);
        record.Values["total"].Should().Be(12.50m);
        record.Values["placed_at"].Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(1700000000), "Debezium's adaptive timestamps are microseconds");
        record.Values["deleted"].Should().Be(false);
    }

    [Fact]
    public void An_update_to_the_same_row_keeps_its_record_id_and_moves_the_revision_forward()
    {
        var result = Reader().Read([Event("c", null, Row(1)), Event("u", Row(1), Row(1, status: "PAID"), lsn: 1500)]);

        result.Records.Should().HaveCount(2);
        result.Records[1].EventId.Should().Be(result.Records[0].EventId, "one row is one warehouse row");
        result.Records[1].Revision.Should().BeGreaterThan(result.Records[0].Revision);
        result.Records[1].Values["status"].Should().Be("PAID");
    }

    [Fact]
    public void A_delete_is_a_record_of_the_deleted_row_marked_deleted()
    {
        var result = Reader().Read([Event("d", Row(7, status: "CANCELLED"), null, lsn: 2000)]);

        var record = result.Records.Should().ContainSingle().Subject;
        record.Values["id"].Should().Be(7L);
        record.Values["deleted"].Should().Be(true, "a hard delete must reach the warehouse");
        record.Revision.Should().Be(2000);
    }

    [Fact]
    public void A_snapshot_read_is_a_record_and_other_operations_and_tables_are_ignored()
    {
        var result = Reader().Read(
        [
            Event("r", null, Row(3)),
            "{\"before\":null,\"after\":null,\"source\":{\"table\":\"orders\",\"schema\":\"public\",\"ts_ms\":1},\"op\":\"t\",\"ts_ms\":1}",
            Event("c", null, Row(4), table: "invoices"),
        ]);

        result.Records.Should().ContainSingle().Which.Values["id"].Should().Be(3L);
        result.Ignored.Should().Be(2, "a truncate and a table without a mapping need no record");
        result.Problems.Should().BeEmpty();
    }

    [Fact]
    public void A_change_without_a_valid_company_is_a_problem_and_the_rest_of_the_batch_still_reads()
    {
        var noTenant = Event("c", null, "{\"id\":9,\"tenant_id\":\"not-a-guid\",\"total\":\"1\",\"status\":\"NEW\",\"placed_at\":null}");

        var result = Reader().Read([noTenant, Event("c", null, Row(10))]);

        result.Records.Should().ContainSingle().Which.Values["id"].Should().Be(10L);
        result.Problems.Should().ContainSingle().Which.Should().Contain("Event 1").And.Contain("company id");
    }

    [Fact]
    public void Messages_that_are_not_json_or_not_changes_are_problems_not_exceptions()
    {
        var result = Reader().Read(["not json at all", "{\"hello\":1}", Event("c", null, Row(11))]);

        result.Records.Should().ContainSingle();
        result.Problems.Should().HaveCount(2);
        result.Problems[0].Should().Contain("not JSON");
        result.Problems[1].Should().Contain("not a Debezium change event");
    }

    [Fact]
    public void A_decimal_sent_as_binary_is_refused_rather_than_read_wrongly()
    {
        var binary = Event("c", null, "{\"id\":12,\"tenant_id\":\"" + Tenant.ToString("D") + "\",\"total\":\"AMOV\",\"status\":\"NEW\",\"placed_at\":null}");

        var result = Reader().Read([binary]);

        result.Records.Should().BeEmpty();
        result.Problems.Should().ContainSingle().Which.Should().Contain("'total' cannot be read as Decimal");
    }

    [Fact]
    public void A_required_column_missing_from_the_row_is_a_problem()
    {
        var missing = Event("c", null, "{\"id\":13,\"tenant_id\":\"" + Tenant.ToString("D") + "\",\"total\":\"1\",\"placed_at\":null}");

        var result = Reader().Read([missing]);

        result.Problems.Should().ContainSingle().Which.Should().Contain("required column 'status'");
    }

    [Fact]
    public void The_schemas_enabled_payload_form_reads_the_same_change()
    {
        var wrapped = "{\"schema\":{},\"payload\":" + Event("c", null, Row(14)) + "}";

        var result = Reader().Read([wrapped]);

        result.Records.Should().ContainSingle().Which.Values["id"].Should().Be(14L);
    }

    [Fact]
    public void A_mapping_that_cannot_work_fails_when_the_reader_is_built()
    {
        var badMarker = new DebeziumTableMapping("orders", Orders, KeyColumn: "id", DeletedField: "total");
        var noTable = new DebeziumTableMapping(" ", Orders, KeyColumn: "id");

        var markerNotBoolean = () => new DebeziumChangeReader([badMarker]);
        var tableMissing = () => new DebeziumChangeReader([noTable]);

        markerNotBoolean.Should().Throw<ArgumentException>().WithMessage("*Boolean*");
        tableMissing.Should().Throw<ArgumentException>().WithMessage("*source table*");
    }
}
