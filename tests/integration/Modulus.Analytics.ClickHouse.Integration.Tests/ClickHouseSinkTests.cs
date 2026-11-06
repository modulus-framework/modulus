namespace Modulus.Analytics.ClickHouse.Integration.Tests;

using FluentAssertions;
using Microsoft.Extensions.Options;
using Modulus.Analytics.Testing;
using Modulus.Core.Abstractions.DataProtection;
using Modulus.Core.Abstractions.Entities;
using Xunit;

[Collection(ClickHouseCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClickHouseSinkTests(ClickHouseFixture clickHouse)
{
    private sealed class FakeProtector : IPersonalDataProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;

        public string Hash(string value) => "hash:" + value;
    }

    private static readonly AnalyticsDatasetDescriptor Everything = new("everything", 1, "qa",
    [
        new("a_guid", AnalyticsFieldType.Guid),
        new("a_decimal", AnalyticsFieldType.Decimal),
        new("a_double", AnalyticsFieldType.Double),
        new("a_long", AnalyticsFieldType.Int64),
        new("a_bool", AnalyticsFieldType.Boolean),
        new("a_timestamp", AnalyticsFieldType.Timestamp),
        new("a_date", AnalyticsFieldType.Date),
        new("a_text", AnalyticsFieldType.String),
        new("a_null", AnalyticsFieldType.String, IsNullable: true),
        new("an_email", AnalyticsFieldType.String, Classification: FieldClassification.Confidential),
        new("a_secret", AnalyticsFieldType.String, IsNullable: true, Classification: FieldClassification.Restricted),
    ],
    Description: "Every field type",
    RetentionDays: 30);

    private async Task<string> Scalar(string sql) => Convert.ToString(await clickHouse.Client.ExecuteScalarAsync(sql), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Prefix(string name) => "t_" + name + "_";

    [Fact]
    public async Task The_real_sink_passes_the_conformance_suite()
    {
        var sink = clickHouse.CreateSink(Prefix("conf"));
        var dataset = new AnalyticsDatasetDescriptor("sample", 1, "qa", [new("label", AnalyticsFieldType.String)]);

        var report = await AnalyticsSinkConformance.RunAsync(
            sink,
            dataset,
            async (tenant, name) => Convert.ToInt64(await clickHouse.Client.ExecuteScalarAsync($"SELECT count() FROM `t_conf_{name}_v1` FINAL WHERE tenant_id = '{tenant}'"), System.Globalization.CultureInfo.InvariantCulture),
            (id, label) => new AnalyticsRecord(id, "sample", 1, Guid.NewGuid(), DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["label"] = label }));

        report.Passed.Should().BeTrue(report.ToString());
    }

    [Fact]
    public async Task Erasing_one_company_deletes_only_its_rows_and_a_second_erase_removes_nothing()
    {
        var sink = clickHouse.CreateSink(Prefix("erase"));
        var dataset = new AnalyticsDatasetDescriptor("sample", 1, "qa", [new("label", AnalyticsFieldType.String)]);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await sink.EnsureDatasetAsync(dataset, CancellationToken.None);
        await sink.WriteAsync(dataset,
        [
            new AnalyticsRecord(Guid.NewGuid(), "sample", 1, a, DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["label"] = "a1" }),
            new AnalyticsRecord(Guid.NewGuid(), "sample", 1, a, DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["label"] = "a2" }),
            new AnalyticsRecord(Guid.NewGuid(), "sample", 1, b, DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["label"] = "b1" }),
        ], CancellationToken.None);

        (await sink.CountTenantAsync(dataset, a, CancellationToken.None)).Should().Be(2);
        (await sink.EraseTenantAsync(dataset, a, CancellationToken.None)).Should().Be(2);

        (await sink.CountTenantAsync(dataset, a, CancellationToken.None)).Should().Be(0);
        (await sink.CountTenantAsync(dataset, b, CancellationToken.None)).Should().Be(1, "another company's rows are untouched");
        (await sink.EraseTenantAsync(dataset, a, CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task The_profile_counts_distinct_rows_future_records_and_nulls_for_one_company()
    {
        var sink = clickHouse.CreateSink(Prefix("profile"));
        var dataset = new AnalyticsDatasetDescriptor("scored", 1, "qa",
        [
            new("label", AnalyticsFieldType.String, IsNullable: true, MaxNullRate: 0.5),
            new("score", AnalyticsFieldType.Int64, IsNullable: true),
        ]);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await sink.EnsureDatasetAsync(dataset, CancellationToken.None);
        await sink.WriteAsync(dataset,
        [
            new AnalyticsRecord(Guid.NewGuid(), "scored", 1, a, now, new Dictionary<string, object?> { ["label"] = null, ["score"] = 1L }),
            new AnalyticsRecord(Guid.NewGuid(), "scored", 1, a, now, new Dictionary<string, object?> { ["label"] = "x", ["score"] = 2L }),
            new AnalyticsRecord(Guid.NewGuid(), "scored", 1, a, now.AddHours(2), new Dictionary<string, object?> { ["label"] = null, ["score"] = null }),
            new AnalyticsRecord(Guid.NewGuid(), "scored", 1, b, now, new Dictionary<string, object?> { ["label"] = "y", ["score"] = 3L }),
        ], CancellationToken.None);

        var profile = await sink.ProfileAsync(dataset, a, now.AddMinutes(5), CancellationToken.None);

        profile.RowCount.Should().Be(3);
        profile.FutureRows.Should().Be(1, "only the record two hours ahead is beyond the allowed skew");
        profile.NullRows.Should().ContainKey("label").WhoseValue.Should().Be(2);
        profile.NullRows.Should().NotContainKey("score", "only fields that declare a null-rate rule are counted");
        (await sink.ProfileAsync(dataset, Guid.NewGuid(), now, CancellationToken.None)).RowCount.Should().Be(0);
    }

    [Fact]
    public async Task A_dataset_that_was_never_created_counts_and_erases_as_zero()
    {
        var sink = clickHouse.CreateSink(Prefix("never"));
        var dataset = new AnalyticsDatasetDescriptor("absent", 1, "qa", [new("label", AnalyticsFieldType.String)]);
        var tenant = Guid.NewGuid();

        (await sink.CountTenantAsync(dataset, tenant, CancellationToken.None)).Should().Be(0);
        (await sink.EraseTenantAsync(dataset, tenant, CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Every_field_type_round_trips_and_masking_is_applied_on_the_way_in()
    {
        var sink = clickHouse.CreateSink(Prefix("types"));
        var tenant = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var guid = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 11, 3, 10, 30, 15, TimeSpan.FromHours(2)).AddTicks(1234560);
        var record = new AnalyticsRecord(eventId, "everything", 1, tenant, at, new Dictionary<string, object?>
        {
            ["a_guid"] = guid,
            ["a_decimal"] = 1234.567890123m,
            ["a_double"] = 2.5,
            ["a_long"] = long.MaxValue,
            ["a_bool"] = true,
            ["a_timestamp"] = at,
            ["a_date"] = new DateOnly(2026, 11, 3),
            ["a_text"] = "héllo 'quoted' \\ ✓",
            ["a_null"] = null,
            ["an_email"] = "a@b.c",
            ["a_secret"] = "123-45-6789",
        });
        var masked = AnalyticsMasking.Apply(Everything, record, new FakeProtector());

        await sink.EnsureDatasetAsync(Everything, CancellationToken.None);
        await sink.WriteAsync(Everything, [masked], CancellationToken.None);

        var t = "`t_types_everything_v1`";
        var where = $"WHERE tenant_id = '{tenant}' AND event_id = '{eventId}'";
        (await Scalar($"SELECT count() FROM {t} FINAL {where}")).Should().Be("1");
        (await Scalar($"SELECT toString(a_guid) FROM {t} FINAL {where}")).Should().Be(guid.ToString());
        (await Scalar($"SELECT toString(a_decimal) FROM {t} FINAL {where}")).Should().Be("1234.567890123");
        (await Scalar($"SELECT a_double FROM {t} FINAL {where}")).Should().Be("2.5");
        (await Scalar($"SELECT a_long FROM {t} FINAL {where}")).Should().Be(long.MaxValue.ToString());
        (await Scalar($"SELECT a_bool FROM {t} FINAL {where}")).Should().Be("True");
        (await Scalar($"SELECT toString(a_date) FROM {t} FINAL {where}")).Should().Be("2026-11-03");
        (await Scalar($"SELECT formatDateTime(a_timestamp, '%Y-%m-%d %H:%i:%S') FROM {t} FINAL {where}")).Should().Be("2026-11-03 08:30:15");
        (await Scalar($"SELECT toUnixTimestamp64Micro(a_timestamp) FROM {t} FINAL {where}")).Should().Be(((at.UtcTicks - DateTime.UnixEpoch.Ticks) / 10).ToString(System.Globalization.CultureInfo.InvariantCulture), "the timestamp keeps microseconds");
        (await Scalar($"SELECT a_text FROM {t} FINAL {where}")).Should().Be("héllo 'quoted' \\ ✓");
        (await Scalar($"SELECT isNull(a_null) FROM {t} FINAL {where}")).Should().Be("1");
        (await Scalar($"SELECT an_email FROM {t} FINAL {where}")).Should().Be("hash:a@b.c", "confidential text is stored hashed");
        (await Scalar($"SELECT count() FROM system.columns WHERE database = currentDatabase() AND table = 't_types_everything_v1' AND name = 'a_secret'"))
            .Should().Be("0", "restricted fields never reach the warehouse");
    }

    [Fact]
    public async Task The_table_carries_its_contract_in_comments_and_retention()
    {
        var sink = clickHouse.CreateSink(Prefix("meta"));

        await sink.EnsureDatasetAsync(Everything, CancellationToken.None);

        (await Scalar("SELECT comment FROM system.columns WHERE database = currentDatabase() AND table = 't_meta_everything_v1' AND name = 'an_email'"))
            .Should().Be("Confidential; Hash");
        (await Scalar("SELECT comment FROM system.tables WHERE database = currentDatabase() AND name = 't_meta_everything_v1'"))
            .Should().Be("owner=qa; dataset=everything.v1");
        (await Scalar("SELECT create_table_query FROM system.tables WHERE database = currentDatabase() AND name = 't_meta_everything_v1'"))
            .Should().Contain("TTL").And.Contain("ReplacingMergeTree");
    }

    [Fact]
    public async Task Setting_a_dataset_up_twice_is_harmless()
    {
        var sink = clickHouse.CreateSink(Prefix("twice"));

        await sink.EnsureDatasetAsync(Everything, CancellationToken.None);
        var again = () => sink.EnsureDatasetAsync(Everything, CancellationToken.None);

        await again.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_table_that_does_not_match_the_contract_is_refused_permanently()
    {
        await clickHouse.Client.ExecuteNonQueryAsync("CREATE TABLE `t_drift_everything_v1` (tenant_id UUID, event_id UUID, occurred_at DateTime64(3, 'UTC'), ingested_at DateTime64(3, 'UTC'), a_guid String) ENGINE = MergeTree ORDER BY tenant_id");
        var sink = clickHouse.CreateSink(Prefix("drift"));

        var act = () => sink.EnsureDatasetAsync(Everything, CancellationToken.None);

        var error = (await act.Should().ThrowAsync<AnalyticsSinkException>()).Which;
        error.IsTransient.Should().BeFalse();
        error.Message.Should().Contain("does not match").And.Contain("column a_guid is String").And.Contain("missing column a_decimal").And.Contain("publish a new version");
    }

    [Fact]
    public async Task When_the_sink_may_not_create_tables_it_verifies_and_reports_a_missing_one()
    {
        var verifyOnly = clickHouse.CreateSink(Prefix("dba"), o => o.CreateTables = false);

        var missing = () => verifyOnly.EnsureDatasetAsync(Everything, CancellationToken.None);
        (await missing.Should().ThrowAsync<AnalyticsSinkException>()).Which.IsTransient.Should().BeFalse();

        await clickHouse.CreateSink(Prefix("dba")).EnsureDatasetAsync(Everything, CancellationToken.None);
        var present = () => verifyOnly.EnsureDatasetAsync(Everything, CancellationToken.None);
        await present.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_field_named_like_a_system_column_is_refused_before_anything_is_created()
    {
        var sink = clickHouse.CreateSink(Prefix("clash"));
        var dataset = new AnalyticsDatasetDescriptor("clash", 1, "qa", [new("tenant_id", AnalyticsFieldType.Guid)]);

        var act = () => sink.EnsureDatasetAsync(dataset, CancellationToken.None);

        (await act.Should().ThrowAsync<AnalyticsSinkException>().WithMessage("*reserved*")).Which.IsTransient.Should().BeFalse();
        (await Scalar("SELECT count() FROM system.tables WHERE database = currentDatabase() AND name = 't_clash_clash_v1'")).Should().Be("0");
    }

    [Fact]
    public async Task An_unreachable_server_is_a_transient_failure()
    {
        var sink = clickHouse.CreateSink(Prefix("down"), connectionString: "Host=127.0.0.1;Port=1;Protocol=http;Username=x;Password=x");

        var act = () => sink.WriteAsync(Everything, [new AnalyticsRecord(Guid.NewGuid(), "everything", 1, Guid.NewGuid(), DateTimeOffset.UtcNow, new Dictionary<string, object?>())], CancellationToken.None);

        (await act.Should().ThrowAsync<AnalyticsSinkException>()).Which.IsTransient.Should().BeTrue();
    }

    [Fact]
    public async Task Writing_to_a_table_that_was_never_set_up_is_a_permanent_failure()
    {
        var sink = clickHouse.CreateSink(Prefix("never"));
        var record = new AnalyticsRecord(Guid.NewGuid(), "everything", 1, Guid.NewGuid(), DateTimeOffset.UtcNow, new Dictionary<string, object?>());

        var act = () => sink.WriteAsync(Everything, [record], CancellationToken.None);

        (await act.Should().ThrowAsync<AnalyticsSinkException>()).Which.IsTransient.Should().BeFalse();
    }

    [Fact]
    public async Task The_dispatcher_delivers_masked_batches_end_to_end_and_redelivery_collapses()
    {
        var sink = clickHouse.CreateSink(Prefix("e2e"));
        var catalog = new AnalyticsDatasetCatalog();
        catalog.Add(Everything);
        var dispatcher = new AnalyticsDispatcher(catalog, [sink], [], new FakeProtector());
        var tenant = Guid.NewGuid();
        AnalyticsRecord Make(Guid id) => new(id, "everything", 1, tenant, DateTimeOffset.UtcNow, new Dictionary<string, object?>
        {
            ["a_guid"] = Guid.NewGuid(),
            ["a_decimal"] = 1m,
            ["a_double"] = 1.0,
            ["a_long"] = 1L,
            ["a_bool"] = false,
            ["a_timestamp"] = DateTimeOffset.UtcNow,
            ["a_date"] = new DateOnly(2026, 1, 1),
            ["a_text"] = "x",
            ["an_email"] = "p@q.r",
            ["a_secret"] = "s",
        });
        var first = Make(Guid.NewGuid());

        var one = await dispatcher.DispatchAsync([first, Make(Guid.NewGuid())]);
        var two = await dispatcher.DispatchAsync([first]);

        one.IsComplete.Should().BeTrue();
        two.IsComplete.Should().BeTrue();
        (await Scalar($"SELECT count() FROM `t_e2e_everything_v1` FINAL WHERE tenant_id = '{tenant}'")).Should().Be("2");
        (await Scalar($"SELECT count() FROM `t_e2e_everything_v1` WHERE tenant_id = '{tenant}' AND an_email = 'p@q.r'")).Should().Be("0", "the plain email never reaches ClickHouse");
    }

    [Fact]
    public async Task The_sink_counts_distinct_records_per_tenant_so_a_warehouse_can_be_reconciled()
    {
        var sink = clickHouse.CreateSink(Prefix("count"));
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        AnalyticsRecord Make(Guid tenant, Guid? id = null) => AnalyticsMasking.Apply(Everything, new AnalyticsRecord(id ?? Guid.NewGuid(), "everything", 1, tenant, DateTimeOffset.UtcNow, new Dictionary<string, object?>
        {
            ["a_guid"] = Guid.NewGuid(),
            ["a_decimal"] = 1m,
            ["a_double"] = 1.0,
            ["a_long"] = 1L,
            ["a_bool"] = false,
            ["a_timestamp"] = DateTimeOffset.UtcNow,
            ["a_date"] = new DateOnly(2026, 1, 1),
            ["a_text"] = "x",
            ["an_email"] = "p@q.r",
        }), new FakeProtector());
        await sink.EnsureDatasetAsync(Everything, CancellationToken.None);
        var repeated = Make(a);

        await sink.WriteAsync(Everything, [repeated, Make(a), Make(b)], CancellationToken.None);
        await sink.WriteAsync(Everything, [repeated], CancellationToken.None);

        (await sink.CountAsync(Everything, a, CancellationToken.None)).Should().Be(2, "the repeated record counts once");
        (await sink.CountAsync(Everything, b, CancellationToken.None)).Should().Be(1);
        (await sink.CountAsync(Everything, Guid.NewGuid(), CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Counting_a_table_that_does_not_exist_is_a_permanent_failure()
    {
        var sink = clickHouse.CreateSink(Prefix("nocount"));

        var act = () => sink.CountAsync(Everything, Guid.NewGuid(), CancellationToken.None);

        (await act.Should().ThrowAsync<AnalyticsSinkException>()).Which.IsTransient.Should().BeFalse();
    }

    [Fact]
    public async Task Wrong_credentials_are_a_permanent_failure()
    {
        var parts = clickHouse.ConnectionString.Split(';').Where(p => !p.StartsWith("Password=", StringComparison.OrdinalIgnoreCase));
        var sink = clickHouse.CreateSink(Prefix("auth"), connectionString: string.Join(';', parts) + ";Password=definitely-wrong");

        var act = () => sink.EnsureDatasetAsync(Everything, CancellationToken.None);

        (await act.Should().ThrowAsync<AnalyticsSinkException>()).Which.IsTransient.Should().BeFalse();
    }
}
