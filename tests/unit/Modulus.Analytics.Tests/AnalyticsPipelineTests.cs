namespace Modulus.Analytics.Tests;

using System.Text.Json;
using FluentAssertions;
using Modulus.Analytics.Testing;
using Modulus.Core.Abstractions.DataProtection;
using Modulus.Core.Abstractions.Entities;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AnalyticsPipelineTests
{
    private sealed class FakeProtector : IPersonalDataProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;

        public string Hash(string value) => "h:" + value;
    }

    // v1 had a single "name" field; v2 splits nothing but renames it to "customer_name" (confidential) and adds "total".
    private static readonly AnalyticsDatasetDescriptor V1 = new("orders", 1, "sales", [new("name", AnalyticsFieldType.String)]);

    private static readonly AnalyticsDatasetDescriptor V2 = new("orders", 2, "sales",
    [
        new("customer_name", AnalyticsFieldType.String, Classification: FieldClassification.Confidential),
        new("ssn", AnalyticsFieldType.String, IsNullable: true, Classification: FieldClassification.Restricted),
        new("total", AnalyticsFieldType.Decimal, IsNullable: true),
    ]);

    private sealed class RenameNameStep : IAnalyticsRecordUpcaster
    {
        public string Dataset => "orders";

        public int FromVersion => 1;

        public IReadOnlyDictionary<string, object?> Upcast(IReadOnlyDictionary<string, object?> values)
            => new Dictionary<string, object?> { ["customer_name"] = values["name"], ["total"] = null };
    }

    private static AnalyticsRecord Rec(int version, Guid? tenant = null, Guid? id = null, Dictionary<string, object?>? values = null)
        => new(id ?? Guid.NewGuid(), "orders", version, tenant ?? Guid.NewGuid(), DateTimeOffset.UtcNow,
            values ?? (version == 1 ? new() { ["name"] = "Ada" } : new() { ["customer_name"] = "Ada", ["ssn"] = "1", ["total"] = 5m }));

    private static AnalyticsDispatcher Dispatcher(IEnumerable<IAnalyticsSink> sinks, params IAnalyticsRecordUpcaster[] upcasters)
    {
        var catalog = new AnalyticsDatasetCatalog();
        catalog.Add(V1);
        catalog.Add(V2);
        return new AnalyticsDispatcher(catalog, sinks, upcasters, new FakeProtector());
    }

    [Fact]
    public void Upcasting_chains_steps_to_the_target_version()
    {
        var result = AnalyticsUpcasting.UpcastTo([new RenameNameStep()], Rec(1), 2);

        result.SchemaVersion.Should().Be(2);
        result.Values.Should().ContainKey("customer_name").And.NotContainKey("name");
    }

    [Fact]
    public void Upcasting_a_current_record_returns_it_unchanged()
    {
        var record = Rec(2);
        AnalyticsUpcasting.UpcastTo([], record, 2).Should().BeSameAs(record);
    }

    [Fact]
    public void Upcasting_fails_for_a_missing_step_or_a_newer_record()
    {
        var missing = () => AnalyticsUpcasting.UpcastTo([], Rec(1), 2);
        var newer = () => AnalyticsUpcasting.UpcastTo([], Rec(3), 2);

        missing.Should().Throw<InvalidOperationException>().WithMessage("*No upcaster*");
        newer.Should().Throw<InvalidOperationException>().WithMessage("*newer than*");
    }

    [Fact]
    public void Json_schema_describes_the_delivered_shape_after_masking()
    {
        using var doc = JsonDocument.Parse(AnalyticsDatasetSchema.ToJsonSchema(V2 with { Description = "Orders", FreshnessSla = TimeSpan.FromMinutes(15), RetentionDays = 90 }));
        var root = doc.RootElement;

        root.GetProperty("$id").GetString().Should().Be("urn:modulus:analytics:orders.v2");
        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        root.GetProperty("x-freshness-sla").GetString().Should().Be("PT15M");
        root.GetProperty("x-retention-days").GetInt32().Should().Be(90);

        var props = root.GetProperty("properties");
        props.TryGetProperty("ssn", out _).Should().BeFalse("restricted fields are dropped");
        props.GetProperty("customer_name").GetProperty("type").GetString().Should().Be("string");
        props.GetProperty("customer_name").GetProperty("x-masking").GetString().Should().Be("Hash");
        props.GetProperty("total").GetProperty("type").EnumerateArray().Select(e => e.GetString()).Should().Equal("number", "null");
        root.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().Equal("customer_name");
    }

    [Theory]
    [InlineData(AnalyticsFieldType.Timestamp, "date-time")]
    [InlineData(AnalyticsFieldType.Date, "date")]
    [InlineData(AnalyticsFieldType.Guid, "uuid")]
    public void Json_schema_gives_temporal_and_guid_fields_a_format(AnalyticsFieldType type, string format)
    {
        using var doc = JsonDocument.Parse(AnalyticsDatasetSchema.ToJsonSchema(new("d", 1, "o", [new("f", type)])));
        doc.RootElement.GetProperty("properties").GetProperty("f").GetProperty("format").GetString().Should().Be(format);
    }

    [Fact]
    public async Task Dispatcher_upgrades_masks_and_writes_to_every_sink()
    {
        var a = new InMemoryAnalyticsSink("a");
        var b = new InMemoryAnalyticsSink("b");
        var dispatcher = Dispatcher([a, b], new RenameNameStep());

        var result = await dispatcher.DispatchAsync([Rec(1), Rec(2)]);

        result.IsComplete.Should().BeTrue();
        result.Accepted.Should().Be(2);
        foreach (var sink in new[] { a, b })
        {
            sink.Rows.Should().HaveCount(2).And.OnlyContain(r => r.SchemaVersion == 2);
            sink.Rows.Should().OnlyContain(r => (string)r.Values["customer_name"]! == "h:Ada" && !r.Values.ContainsKey("ssn"));
            sink.Datasets.Keys.Should().Equal("orders.v2");
        }
    }

    [Fact]
    public async Task Dispatcher_sets_a_dataset_up_once_per_sink()
    {
        var sink = new InMemoryAnalyticsSink();
        var dispatcher = Dispatcher([sink]);

        await dispatcher.DispatchAsync([Rec(2)]);
        await dispatcher.DispatchAsync([Rec(2)]);

        sink.EnsureCalls.Should().Be(1);
    }

    [Fact]
    public async Task Dispatcher_rejects_records_it_cannot_place_and_still_sends_the_rest()
    {
        var sink = new InMemoryAnalyticsSink();
        var dispatcher = Dispatcher([sink]);
        var noTenant = Rec(2) with { TenantId = Guid.Empty };
        var unknown = Rec(2) with { Dataset = "nope" };
        var tooOld = Rec(1);

        var result = await dispatcher.DispatchAsync([noTenant, unknown, tooOld, Rec(2)]);

        result.Accepted.Should().Be(1);
        result.Rejected.Select(r => r.Reason).Should().SatisfyRespectively(
            r => r.Should().Contain("tenant"),
            r => r.Should().Contain("not registered"),
            r => r.Should().Contain("No upcaster"));
        result.IsComplete.Should().BeFalse();
        sink.Rows.Should().HaveCount(1);
    }

    [Fact]
    public async Task One_failing_sink_does_not_stop_the_others_and_reports_transience()
    {
        var bad = new InMemoryAnalyticsSink("bad") { FailWith = new AnalyticsSinkException("throttled", isTransient: true) };
        var worse = new InMemoryAnalyticsSink("worse") { FailWith = new AnalyticsSinkException("bad schema", isTransient: false) };
        var crash = new InMemoryAnalyticsSink("crash") { FailWith = new TimeoutException("slow") };
        var good = new InMemoryAnalyticsSink("good");
        var dispatcher = Dispatcher([bad, worse, crash, good]);

        var result = await dispatcher.DispatchAsync([Rec(2)]);

        good.Rows.Should().HaveCount(1);
        result.Outcomes.Single(o => o.Sink == "good").Succeeded.Should().BeTrue();
        result.Outcomes.Single(o => o.Sink == "bad").IsTransient.Should().BeTrue();
        result.Outcomes.Single(o => o.Sink == "worse").IsTransient.Should().BeFalse();
        result.Outcomes.Single(o => o.Sink == "crash").IsTransient.Should().BeTrue("an unexpected provider error is retried");
        result.ShouldRetry.Should().BeTrue();
        result.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task Redelivering_the_same_record_does_not_duplicate_it()
    {
        var sink = new InMemoryAnalyticsSink();
        var dispatcher = Dispatcher([sink]);
        var record = Rec(2);

        await dispatcher.DispatchAsync([record]);
        await dispatcher.DispatchAsync([record]);

        sink.Rows.Should().HaveCount(1);
    }

    [Fact]
    public async Task In_memory_sink_passes_the_conformance_suite()
    {
        var sink = new InMemoryAnalyticsSink();

        var report = await AnalyticsSinkConformance.RunAsync(
            sink,
            V1,
            (tenant, dataset) => Task.FromResult((long)sink.Rows.Count(r => r.TenantId == tenant && r.Dataset == dataset)),
            (id, label) => new AnalyticsRecord(id, "orders", 1, Guid.NewGuid(), DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["name"] = label }));

        report.Passed.Should().BeTrue(report.ToString());
        report.Checks.Should().HaveCount(6);
    }

    [Fact]
    public async Task Conformance_suite_catches_a_sink_that_duplicates_and_mixes_tenants()
    {
        var rows = new List<AnalyticsRecord>();
        var sink = new AppendOnlySink(rows);

        var report = await AnalyticsSinkConformance.RunAsync(
            sink,
            V1,
            (_, dataset) => Task.FromResult((long)rows.Count(r => r.Dataset == dataset)),
            (id, label) => new AnalyticsRecord(id, "orders", 1, Guid.NewGuid(), DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["name"] = label }));

        report.Passed.Should().BeFalse();
        report.Failures.Select(f => f.Name).Should().Contain(["Writing the same record again does not duplicate it", "Tenants are kept apart"]);
    }

    private sealed class AppendOnlySink(List<AnalyticsRecord> rows) : IAnalyticsSink
    {
        public string Name => "append";

        public Task EnsureDatasetAsync(AnalyticsDatasetDescriptor dataset, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(AnalyticsDatasetDescriptor dataset, IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken)
        {
            rows.AddRange(records);
            return Task.CompletedTask;
        }
    }
}
