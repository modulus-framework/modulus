namespace Modulus.Analytics.ClickHouse.Tests;

using System.Data.Common;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Entities;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ClickHouseDdlTests
{
    private static readonly ClickHouseSinkOptions Options = new();

    private static readonly AnalyticsDatasetDescriptor Orders = new("orders", 2, "sales team's",
    [
        new("order_id", AnalyticsFieldType.Guid),
        new("total", AnalyticsFieldType.Decimal),
        new("email", AnalyticsFieldType.String, Classification: FieldClassification.Confidential),
        new("ssn", AnalyticsFieldType.String, IsNullable: true, Classification: FieldClassification.Restricted),
        new("placed_on", AnalyticsFieldType.Date, IsNullable: true),
        new("placed_at", AnalyticsFieldType.Timestamp),
        new("quantity", AnalyticsFieldType.Int64),
        new("ratio", AnalyticsFieldType.Double),
        new("paid", AnalyticsFieldType.Boolean),
        new("note", AnalyticsFieldType.String, IsNullable: true, Classification: FieldClassification.Confidential),
    ],
    RetentionDays: 90);

    private sealed class ServerError(int code) : DbException("server error")
    {
        public override int ErrorCode => code;
    }

    [Fact]
    public void Table_name_is_prefix_dataset_and_version()
        => ClickHouseDdl.TableName(new ClickHouseSinkOptions { TablePrefix = "mod_" }, Orders).Should().Be("mod_orders_v2");

    [Fact]
    public void Columns_follow_the_masking_policy_and_nullability()
    {
        var columns = ClickHouseDdl.Columns(Orders, Options).ToDictionary(c => c.Name, c => c.Type);

        columns.Keys.Should().NotContain("ssn", "restricted fields are dropped");
        columns["email"].Should().Be("String", "a hashed field is text");
        columns["note"].Should().Be("Nullable(String)");
        columns["order_id"].Should().Be("UUID");
        columns["total"].Should().Be("Decimal(38, 9)");
        columns["placed_on"].Should().Be("Nullable(Date)");
        columns["placed_at"].Should().Be("DateTime64(6, 'UTC')");
        columns["quantity"].Should().Be("Int64");
        columns["ratio"].Should().Be("Float64");
        columns["paid"].Should().Be("Bool");
        columns["tenant_id"].Should().Be("UUID");
        ClickHouseDdl.Columns(Orders, Options).Take(4).Select(c => c.Name).Should().Equal("tenant_id", "event_id", "occurred_at", "ingested_at");
    }

    [Fact]
    public void Decimal_precision_and_scale_come_from_the_options()
        => ClickHouseDdl.Columns(Orders, new ClickHouseSinkOptions { DecimalPrecision = 18, DecimalScale = 4 })
            .Single(c => c.Name == "total").Type.Should().Be("Decimal(18, 4)");

    [Fact]
    public void Create_table_is_tenant_keyed_deduplicating_and_idempotent()
    {
        var sql = ClickHouseDdl.CreateTable(Orders, Options);

        sql.Should().StartWith("CREATE TABLE IF NOT EXISTS `orders_v2`");
        sql.Should().Contain("`ingested_at` DateTime64(3, 'UTC') DEFAULT now64(3)");
        sql.Should().Contain("ENGINE = ReplacingMergeTree(`ingested_at`)");
        sql.Should().Contain("PARTITION BY toYYYYMM(`occurred_at`)");
        sql.Should().Contain("ORDER BY (`tenant_id`, toDate(`occurred_at`), `event_id`)");
        sql.Should().Contain("TTL toDateTime(`ingested_at`) + INTERVAL 90 DAY");
        sql.Should().NotContain("ssn");
    }

    [Fact]
    public void Columns_record_their_classification_and_masking_and_the_table_its_owner()
    {
        var sql = ClickHouseDdl.CreateTable(Orders, Options);

        sql.Should().Contain("`email` String COMMENT 'Confidential; Hash'");
        sql.Should().Contain("`total` Decimal(38, 9) COMMENT 'Public; Keep'");
        sql.Should().EndWith("COMMENT 'owner=sales team\\'s; dataset=orders.v2'", "quotes in text are escaped");
    }

    [Fact]
    public void A_dataset_without_retention_has_no_ttl()
        => ClickHouseDdl.CreateTable(Orders with { RetentionDays = null }, Options).Should().NotContain("TTL");

    [Fact]
    public void Literals_and_identifiers_are_escaped()
    {
        ClickHouseDdl.Literal("a'b\\c").Should().Be("'a\\'b\\\\c'");
        ClickHouseDdl.Quote("a`b").Should().Be("`a``b`");
    }

    [Fact]
    public void Fields_named_like_the_sinks_own_columns_are_reported()
        => ClickHouseDdl.Collisions(new("d", 1, "o", [new("event_id", AnalyticsFieldType.Guid), new("fine", AnalyticsFieldType.String), new("tenant_id", AnalyticsFieldType.Guid)]))
            .Should().Equal("event_id", "tenant_id");

    [Fact]
    public void Diff_reports_missing_wrong_and_unexpected_columns_and_is_empty_for_a_match()
    {
        var expected = ClickHouseDdl.Columns(Orders, Options);
        var actual = expected.ToDictionary(c => c.Name, c => c.Type);
        ClickHouseDdl.Diff(expected, actual).Should().BeEmpty();

        actual.Remove("email");
        actual["total"] = "Float64";
        actual["extra"] = "String";

        ClickHouseDdl.Diff(expected, actual).Should().BeEquivalentTo(
            "missing column email (String)",
            "column total is Float64, expected Decimal(38, 9)",
            "unexpected column extra");
    }

    [Theory]
    [InlineData(60, false)]
    [InlineData(516, false)]
    [InlineData(497, false)]
    [InlineData(53, false)]
    [InlineData(241, true)]
    [InlineData(999, true)]
    public void Server_errors_are_classified_by_code(int code, bool transient)
        => ClickHouseErrors.Translate("write", new ServerError(code)).IsTransient.Should().Be(transient);

    [Fact]
    public void Network_failures_are_transient_and_bad_values_are_permanent()
    {
        ClickHouseErrors.Translate("write", new HttpRequestException("refused")).IsTransient.Should().BeTrue();
        ClickHouseErrors.Translate("write", new TimeoutException()).IsTransient.Should().BeTrue();
        ClickHouseErrors.Translate("write", new InvalidCastException("bad")).IsTransient.Should().BeFalse();
        var known = new AnalyticsSinkException("x", isTransient: false);
        ClickHouseErrors.Translate("write", known).Should().BeSameAs(known);
    }

    [Theory]
    [InlineData("", "", 38, 9, 10000, false)]
    [InlineData("Host=h", "Bad-Prefix", 38, 9, 10000, false)]
    [InlineData("Host=h", "ok_", 0, 0, 10000, false)]
    [InlineData("Host=h", "ok_", 10, 11, 10000, false)]
    [InlineData("Host=h", "ok_", 38, 9, 0, false)]
    [InlineData("Host=h", "ok_", 38, 9, 500, true)]
    public void Options_are_validated_at_startup(string connection, string prefix, int precision, int scale, int batch, bool valid)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analytics:ClickHouse:ConnectionString"] = connection,
            ["Analytics:ClickHouse:TablePrefix"] = prefix,
            ["Analytics:ClickHouse:DecimalPrecision"] = precision.ToString(),
            ["Analytics:ClickHouse:DecimalScale"] = scale.ToString(),
            ["Analytics:ClickHouse:InsertBatchSize"] = batch.ToString(),
        }).Build();
        services.AddModulusAnalytics(a => a.AddClickHouse(configuration));
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<ClickHouseSinkOptions>>().Value;

        if (valid)
        {
            read().ConnectionString.Should().Be("Host=h");
        }
        else
        {
            read.Should().Throw<OptionsValidationException>();
        }
    }

    [Fact]
    public void Registration_adds_one_clickhouse_sink()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analytics:ClickHouse:ConnectionString"] = "Host=localhost;Port=8123",
        }).Build();
        services.AddModulusAnalytics(a => a.AddClickHouse(configuration).AddClickHouse(configuration));
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IAnalyticsSink>().Should().ContainSingle().Which.Name.Should().Be("clickhouse");
    }
}
