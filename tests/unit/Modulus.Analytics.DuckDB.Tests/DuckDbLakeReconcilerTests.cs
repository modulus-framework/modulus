namespace Modulus.Analytics.DuckDb.Tests;

using FluentAssertions;
using Microsoft.Extensions.Options;
using Modulus.Analytics.Parquet;
using Modulus.Core.Abstractions.Entities;
using Modulus.Storage;
using Xunit;

[Trait("Category", "Unit")]
public sealed class DuckDbLakeReconcilerTests : IDisposable
{
    private static readonly AnalyticsDatasetDescriptor Orders = new("orders", 1, "sales",
    [
        new("total", AnalyticsFieldType.Decimal),
        new("email", AnalyticsFieldType.String, IsNullable: true, Classification: FieldClassification.Restricted),
    ]);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "modulus-duckdb-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ParquetAnalyticsSink Sink()
    {
        var storage = new LocalFileStorage(Options.Create(new StorageOptions { BasePath = _root }));
        return new ParquetAnalyticsSink(storage, Options.Create(new ParquetSinkOptions { RootPath = "lake" }));
    }

    private string LakeRoot => Path.Combine(_root, "lake");

    private static AnalyticsRecord Rec(Guid tenant, Guid? id = null, string? email = "a@b.c")
        => new(id ?? Guid.NewGuid(), "orders", 1, tenant, new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero),
            new Dictionary<string, object?> { ["total"] = 5m, ["email"] = email });

    [Fact]
    public async Task A_tenant_is_counted_once_per_distinct_record_across_its_files()
    {
        var tenant = Guid.NewGuid();
        var sink = Sink();
        await sink.EnsureDatasetAsync(Orders, CancellationToken.None);
        var repeated = Guid.NewGuid();
        await sink.WriteAsync(Orders, [Rec(tenant, repeated), Rec(tenant, Guid.NewGuid())], CancellationToken.None);
        await sink.WriteAsync(Orders, [Rec(tenant, repeated), Rec(tenant, Guid.NewGuid())], CancellationToken.None);

        var count = await new DuckDbLakeReconciler().CountDistinctRecordsAsync(
            DuckDbLakeReconciler.DatasetDirectory(LakeRoot, Orders), tenant, CancellationToken.None);

        count.Should().Be(3, "the repeated record counts once even though two files hold it");
    }

    [Fact]
    public async Task Another_tenants_records_are_not_counted()
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        var sink = Sink();
        await sink.EnsureDatasetAsync(Orders, CancellationToken.None);
        await sink.WriteAsync(Orders, [Rec(mine), Rec(mine)], CancellationToken.None);
        await sink.WriteAsync(Orders, [Rec(other), Rec(other), Rec(other)], CancellationToken.None);

        var reconciler = new DuckDbLakeReconciler();
        var directory = DuckDbLakeReconciler.DatasetDirectory(LakeRoot, Orders);

        (await reconciler.CountDistinctRecordsAsync(directory, mine, CancellationToken.None)).Should().Be(2);
        (await reconciler.CountDistinctRecordsAsync(directory, other, CancellationToken.None)).Should().Be(3);
    }

    [Fact]
    public async Task Null_values_are_read_back_without_changing_the_count()
    {
        var tenant = Guid.NewGuid();
        var sink = Sink();
        await sink.EnsureDatasetAsync(Orders, CancellationToken.None);
        await sink.WriteAsync(Orders, [Rec(tenant, email: null), Rec(tenant, email: "x@y.z")], CancellationToken.None);

        var count = await new DuckDbLakeReconciler().CountDistinctRecordsAsync(
            DuckDbLakeReconciler.DatasetDirectory(LakeRoot, Orders), tenant, CancellationToken.None);

        count.Should().Be(2);
    }

    [Fact]
    public async Task A_missing_lake_or_tenant_counts_zero()
    {
        var reconciler = new DuckDbLakeReconciler();

        (await reconciler.CountDistinctRecordsAsync(Path.Combine(_root, "nowhere"), Guid.NewGuid(), CancellationToken.None)).Should().Be(0);

        var sink = Sink();
        await sink.EnsureDatasetAsync(Orders, CancellationToken.None);
        await sink.WriteAsync(Orders, [Rec(Guid.NewGuid())], CancellationToken.None);
        (await reconciler.CountDistinctRecordsAsync(DuckDbLakeReconciler.DatasetDirectory(LakeRoot, Orders), Guid.NewGuid(), CancellationToken.None))
            .Should().Be(0, "that tenant has no partition");
    }

    [Fact]
    public async Task The_empty_tenant_is_refused()
    {
        var act = () => new DuckDbLakeReconciler().CountDistinctRecordsAsync(LakeRoot, Guid.Empty, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void The_dataset_folder_matches_the_sink_layout()
        => DuckDbLakeReconciler.DatasetDirectory("/lake", Orders).Replace('\\', '/').Should().Be("/lake/orders_v1");
}
