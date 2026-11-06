namespace Modulus.Analytics.Tests;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions.DataProtection;
using Modulus.Core.Abstractions.Entities;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AnalyticsContractsTests
{
    private static AnalyticsDatasetDescriptor Orders(int version = 1) => new(
        "sales_orders",
        version,
        "sales",
        [
            new("order_id", AnalyticsFieldType.Guid),
            new("total", AnalyticsFieldType.Decimal, Classification: FieldClassification.Internal),
            new("customer_email", AnalyticsFieldType.String, Classification: FieldClassification.Confidential),
            new("national_id", AnalyticsFieldType.String, IsNullable: true, Classification: FieldClassification.Restricted),
            new("note", AnalyticsFieldType.String, Classification: FieldClassification.Restricted, Masking: AnalyticsMaskingAction.Keep),
        ]);

    private sealed class FakeProtector : IPersonalDataProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;

        public string Hash(string value) => "h:" + value;
    }

    [Fact]
    public void Default_masking_follows_classification()
    {
        AnalyticsMasking.DefaultFor(FieldClassification.Public).Should().Be(AnalyticsMaskingAction.Keep);
        AnalyticsMasking.DefaultFor(FieldClassification.Internal).Should().Be(AnalyticsMaskingAction.Keep);
        AnalyticsMasking.DefaultFor(FieldClassification.Confidential).Should().Be(AnalyticsMaskingAction.Hash);
        AnalyticsMasking.DefaultFor(FieldClassification.Restricted).Should().Be(AnalyticsMaskingAction.Drop);
    }

    [Fact]
    public void Masking_keeps_hashes_and_drops_and_removes_undeclared_fields()
    {
        var id = Guid.NewGuid();
        var record = new AnalyticsRecord(Guid.NewGuid(), "sales_orders", 1, Guid.NewGuid(), DateTimeOffset.UtcNow, new Dictionary<string, object?>
        {
            ["order_id"] = id,
            ["total"] = 12.5m,
            ["customer_email"] = "a@b.c",
            ["national_id"] = "123",
            ["note"] = "kept on purpose",
            ["not_declared"] = "secret",
        });

        var masked = AnalyticsMasking.Apply(Orders(), record, new FakeProtector());

        masked.Values.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["order_id"] = id,
            ["total"] = 12.5m,
            ["customer_email"] = "h:a@b.c",
            ["note"] = "kept on purpose",
        });
        record.Values.Should().ContainKey("national_id", "the input record is not changed");
    }

    [Fact]
    public void Masking_leaves_a_null_hashed_value_null()
    {
        var dataset = new AnalyticsDatasetDescriptor("d", 1, "o", [new("email", AnalyticsFieldType.String, true, FieldClassification.Confidential)]);
        var record = new AnalyticsRecord(Guid.NewGuid(), "d", 1, Guid.NewGuid(), DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["email"] = null });

        AnalyticsMasking.Apply(dataset, record, new FakeProtector()).Values["email"].Should().BeNull();
    }

    [Fact]
    public void A_valid_descriptor_has_no_problems_and_a_versioned_key()
    {
        Orders(2).Validate().Should().BeEmpty();
        Orders(2).Key.Should().Be("sales_orders.v2");
    }

    [Theory]
    [InlineData("Sales")]
    [InlineData("1sales")]
    [InlineData("sales-orders")]
    [InlineData("")]
    public void A_bad_dataset_name_is_reported(string name)
        => (Orders() with { Name = name }).Validate().Should().ContainSingle(p => p.Contains("lower snake case"));

    [Fact]
    public void Duplicate_fields_a_missing_owner_and_a_bad_version_are_reported()
    {
        var dataset = new AnalyticsDatasetDescriptor("d", 0, " ", [new("a", AnalyticsFieldType.Int64), new("a", AnalyticsFieldType.Int64)]);

        var problems = dataset.Validate();

        problems.Should().Contain(p => p.Contains("version")).And.Contain(p => p.Contains("owner")).And.Contain(p => p.Contains("more than once"));
    }

    [Fact]
    public void A_blank_required_feature_is_reported_and_a_named_or_absent_one_is_fine()
    {
        (Orders() with { RequiredFeature = " " }).Validate().Should().ContainSingle(p => p.Contains("required feature"));
        (Orders() with { RequiredFeature = "analytics.advanced" }).Validate().Should().BeEmpty();
        Orders().RequiredFeature.Should().BeNull();
    }

    [Fact]
    public void A_dataset_without_fields_is_reported()
        => new AnalyticsDatasetDescriptor("d", 1, "o", []).Validate().Should().ContainSingle(p => p.Contains("at least one field"));

    [Fact]
    public void Non_positive_sla_and_retention_are_reported()
        => (Orders() with { FreshnessSla = TimeSpan.Zero, RetentionDays = 0 }).Validate().Should().HaveCount(2);

    [Fact]
    public void Catalog_returns_versions_and_the_latest()
    {
        var catalog = new AnalyticsDatasetCatalog();
        catalog.Add(Orders(2));
        catalog.Add(Orders(1));

        catalog.All.Select(d => d.Version).Should().Equal(1, 2);
        catalog.Latest("sales_orders")!.Version.Should().Be(2);
        catalog.TryGet("sales_orders", 1, out var v1).Should().BeTrue();
        v1!.Version.Should().Be(1);
        catalog.TryGet("sales_orders", 3, out _).Should().BeFalse();
        catalog.Latest("missing").Should().BeNull();
    }

    [Fact]
    public void Catalog_rejects_duplicates_and_invalid_descriptors()
    {
        var catalog = new AnalyticsDatasetCatalog();
        catalog.Add(Orders());

        var duplicate = () => catalog.Add(Orders());
        var invalid = () => catalog.Add(Orders() with { Name = "Bad" });

        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*registered twice*");
        invalid.Should().Throw<InvalidOperationException>().WithMessage("*invalid*");
    }

    private sealed class NullSink : IAnalyticsSink
    {
        public string Name => "null";

        public Task EnsureDatasetAsync(AnalyticsDatasetDescriptor dataset, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(AnalyticsDatasetDescriptor dataset, IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void Registration_accumulates_datasets_across_calls_and_registers_sinks_once()
    {
        var services = new ServiceCollection();
        services.AddModulusAnalytics(a => a.AddDataset(Orders(1)).AddSink<NullSink>());
        services.AddModulusAnalytics(a => a.AddDataset(Orders(2)).AddSink<NullSink>());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAnalyticsDatasetCatalog>().All.Should().HaveCount(2);
        provider.GetServices<IAnalyticsSink>().Should().ContainSingle();
    }

    [Fact]
    public void Sink_exception_carries_transience()
        => new AnalyticsSinkException("x", isTransient: true).IsTransient.Should().BeTrue();
}
