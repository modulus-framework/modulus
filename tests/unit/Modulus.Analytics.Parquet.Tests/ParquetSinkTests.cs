namespace Modulus.Analytics.Parquet.Tests;

using System.Globalization;
using FluentAssertions;
using global::Parquet;
using global::Parquet.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Analytics.Testing;
using Modulus.Core.Abstractions.DataProtection;
using Modulus.Core.Abstractions.Entities;
using Modulus.Storage;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ParquetSinkTests : IDisposable
{
    private static readonly AnalyticsDatasetDescriptor Everything = new("everything", 1, "qa",
    [
        new("a_guid", AnalyticsFieldType.Guid),
        new("a_decimal", AnalyticsFieldType.Decimal),
        new("a_double", AnalyticsFieldType.Double),
        new("a_long", AnalyticsFieldType.Int64),
        new("a_long_null", AnalyticsFieldType.Int64, IsNullable: true),
        new("a_bool", AnalyticsFieldType.Boolean),
        new("a_timestamp", AnalyticsFieldType.Timestamp),
        new("a_date", AnalyticsFieldType.Date),
        new("a_text", AnalyticsFieldType.String),
        new("a_text_null", AnalyticsFieldType.String, IsNullable: true),
        new("an_email", AnalyticsFieldType.String, Classification: FieldClassification.Confidential),
        new("a_secret", AnalyticsFieldType.String, IsNullable: true, Classification: FieldClassification.Restricted),
    ],
    Description: "Every type");

    private sealed class FakeProtector : IPersonalDataProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string ciphertext) => ciphertext;

        public string Hash(string value) => "hash:" + value;
    }

    private sealed class FailingStorage(Exception failure) : IFileStorage
    {
        public Task<Stream> DownloadAsync(string path, CancellationToken ct = default) => throw failure;

        public Task UploadAsync(string path, Stream content, string? contentType = null, CancellationToken ct = default) => throw failure;

        public Task DeleteAsync(string path, CancellationToken ct = default) => throw failure;

        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => throw failure;

        public Task<string> GetPresignedUrlAsync(string path, TimeSpan expiry, CancellationToken ct = default) => throw failure;

        public Task<string> GetPresignedUploadUrlAsync(string path, TimeSpan expiry, string? contentType = null, CancellationToken ct = default) => throw failure;
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "modulus-parquet-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public ParquetSinkTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ParquetAnalyticsSink Sink(Action<ParquetSinkOptions>? configure = null)
    {
        var options = new ParquetSinkOptions();
        configure?.Invoke(options);
        var storage = new LocalFileStorage(Options.Create(new StorageOptions { BasePath = _root }));
        return new ParquetAnalyticsSink(storage, Options.Create(options));
    }

    private AnalyticsRecord Record(Guid tenant, Guid? id = null, DateTimeOffset? at = null, long n = 1)
        => new(id ?? Guid.NewGuid(), "everything", 1, tenant, at ?? new DateTimeOffset(2026, 11, 3, 10, 30, 15, TimeSpan.Zero).AddTicks(1234560), new Dictionary<string, object?>
        {
            ["a_guid"] = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ["a_decimal"] = 1234.567890123m,
            ["a_double"] = 2.5,
            ["a_long"] = n,
            ["a_long_null"] = null,
            ["a_bool"] = true,
            ["a_timestamp"] = new DateTimeOffset(2026, 11, 3, 8, 30, 15, TimeSpan.Zero).AddTicks(1234560),
            ["a_date"] = new DateOnly(2026, 11, 3),
            ["a_text"] = "héllo ✓",
            ["a_text_null"] = null,
            ["an_email"] = "a@b.c",
            ["a_secret"] = "123-45-6789",
        });

    private static AnalyticsRecord Masked(AnalyticsRecord record) => AnalyticsMasking.Apply(Everything, record, new FakeProtector());

    private IEnumerable<string> Files(string pattern = "*.parquet")
        => Directory.Exists(_root) ? Directory.EnumerateFiles(_root, pattern, SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal) : [];

    private static async Task<(List<Dictionary<string, object?>> Rows, ParquetReader Reader)> ReadAsync(string path)
    {
        var reader = await ParquetReader.CreateAsync(File.OpenRead(path), new ParquetOptions { UseDateOnlyTypeForDates = true });
        var rows = new List<Dictionary<string, object?>>();
        for (var g = 0; g < reader.RowGroupCount; g++)
        {
            using var group = reader.OpenRowGroupReader(g);
            var count = (int)group.RowCount;
            var start = rows.Count;
            rows.AddRange(Enumerable.Range(0, count).Select(_ => new Dictionary<string, object?>()));
            foreach (var field in reader.Schema.DataFields)
            {
                var column = await ReadColumn(group, field, count);
                for (var i = 0; i < count; i++)
                {
                    rows[start + i][field.Name] = column[i];
                }
            }
        }

        return (rows, reader);
    }

    private static async Task<object?[]> ReadColumn(ParquetRowGroupReader group, DataField field, int count) => field.ClrType.Name switch
    {
        "Int64" => await Read<long>(group, field, count),
        "Decimal" => await Read<decimal>(group, field, count),
        "Double" => await Read<double>(group, field, count),
        "Boolean" => await Read<bool>(group, field, count),
        "DateTime" => await Read<DateTime>(group, field, count),
        "DateOnly" => await Read<DateOnly>(group, field, count),
        _ => await ReadText(group, field, count),
    };

    private static async Task<object?[]> ReadText(ParquetRowGroupReader group, DataField field, int count)
    {
        var values = new string?[count];
        await group.ReadAsync(field, values.AsMemory());
        return [.. values];
    }

    private static async Task<object?[]> Read<T>(ParquetRowGroupReader group, DataField field, int count)
        where T : struct
    {
        if (field.IsNullable)
        {
            var nullable = new T?[count];
            await group.ReadAsync<T>(field, nullable.AsMemory());
            return [.. nullable.Select(v => (object?)v)];
        }

        var values = new T[count];
        await group.ReadAsync<T>(field, values.AsMemory());
        return [.. values.Select(v => (object?)v)];
    }

    [Fact]
    public async Task Files_are_laid_out_by_dataset_version_tenant_and_day()
    {
        var sink = Sink();
        var day1 = new DateTimeOffset(2026, 11, 3, 23, 59, 0, TimeSpan.Zero);
        var day2 = day1.AddMinutes(2);

        await sink.WriteAsync(Everything, [Masked(Record(_tenantA, at: day1)), Masked(Record(_tenantA, at: day2)), Masked(Record(_tenantB, at: day1))], CancellationToken.None);

        var relative = Files().Select(f => Path.GetRelativePath(_root, f).Replace('\\', '/')).ToList();
        relative.Should().HaveCount(3);
        relative.Should().Contain(p => p.StartsWith($"analytics/everything_v1/tenant={_tenantA:D}/dt=2026-11-03/part-", StringComparison.Ordinal));
        relative.Should().Contain(p => p.StartsWith($"analytics/everything_v1/tenant={_tenantA:D}/dt=2026-11-04/part-", StringComparison.Ordinal));
        relative.Should().Contain(p => p.StartsWith($"analytics/everything_v1/tenant={_tenantB:D}/dt=2026-11-03/part-", StringComparison.Ordinal));
        relative.Should().OnlyContain(p => p.EndsWith(".parquet", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_field_type_round_trips_and_masking_shapes_the_columns()
    {
        var sink = Sink();
        var record = Record(_tenantA);

        await sink.WriteAsync(Everything, [Masked(record)], CancellationToken.None);

        var (rows, reader) = await ReadAsync(Files().Single());
        await using (reader)
        {
            var row = rows.Should().ContainSingle().Subject;
            row["tenant_id"].Should().Be(_tenantA.ToString("D"));
            row["event_id"].Should().Be(record.EventId.ToString("D"));
            row["occurred_at"].Should().Be(record.OccurredAt.UtcDateTime);
            row["a_guid"].Should().Be("11111111-2222-3333-4444-555555555555");
            row["a_decimal"].Should().Be(1234.567890123m);
            row["a_double"].Should().Be(2.5);
            row["a_long"].Should().Be(1L);
            row["a_long_null"].Should().BeNull();
            row["a_bool"].Should().Be(true);
            row["a_timestamp"].Should().Be(new DateTimeOffset(2026, 11, 3, 8, 30, 15, TimeSpan.Zero).AddTicks(1234560).UtcDateTime, "microseconds are kept");
            row["a_date"].Should().Be(new DateOnly(2026, 11, 3));
            row["a_text"].Should().Be("héllo ✓");
            row["a_text_null"].Should().BeNull();
            row["an_email"].Should().Be("hash:a@b.c", "confidential text is stored hashed");
            row.Should().NotContainKey("a_secret", "restricted fields never reach the lake");

            var logical = reader.Metadata!.Schema!.ToDictionary(e => e.Name, e => e.ConvertedType?.ToString());
            logical["occurred_at"].Should().Be("TIMESTAMP_MICROS");
            logical["a_timestamp"].Should().Be("TIMESTAMP_MICROS");
            logical["a_date"].Should().Be("DATE");
            logical["a_decimal"].Should().Be("DECIMAL");
            logical["a_text"].Should().Be("UTF8");
        }
    }

    [Fact]
    public async Task System_columns_are_required_and_nullable_fields_are_optional()
    {
        await Sink().WriteAsync(Everything, [Masked(Record(_tenantA))], CancellationToken.None);

        var (_, reader) = await ReadAsync(Files().Single());
        await using (reader)
        {
            var fields = reader.Schema.DataFields.ToDictionary(f => f.Name);
            fields["tenant_id"].IsNullable.Should().BeFalse();
            fields["event_id"].IsNullable.Should().BeFalse();
            fields["occurred_at"].IsNullable.Should().BeFalse();
            fields["a_long"].IsNullable.Should().BeFalse();
            fields["a_long_null"].IsNullable.Should().BeTrue();
            fields["a_text_null"].IsNullable.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Writing_the_same_batch_again_overwrites_the_same_file_whatever_the_order()
    {
        var sink = Sink();
        var a = Masked(Record(_tenantA));
        var b = Masked(Record(_tenantA));

        await sink.WriteAsync(Everything, [a, b], CancellationToken.None);
        await sink.WriteAsync(Everything, [b, a], CancellationToken.None);

        Files().Should().ContainSingle();
        (await ReadAsync(Files().Single())).Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_retry_with_different_records_writes_another_file_so_readers_keep_one_row_per_event()
    {
        var sink = Sink();
        var a = Masked(Record(_tenantA));
        var b = Masked(Record(_tenantA));

        await sink.WriteAsync(Everything, [a], CancellationToken.None);
        await sink.WriteAsync(Everything, [a, b], CancellationToken.None);

        var rows = new List<Dictionary<string, object?>>();
        foreach (var file in Files())
        {
            rows.AddRange((await ReadAsync(file)).Rows);
        }

        Files().Should().HaveCount(2);
        rows.Should().HaveCount(3, "record a is in both files: Parquet is append-only");
        rows.Select(r => (string)r["event_id"]!).Distinct().Should().HaveCount(2, "one row per event after de-duplication");
    }

    [Fact]
    public async Task A_big_batch_is_split_into_files_of_at_most_the_configured_rows()
    {
        var sink = Sink(o => o.MaxRowsPerFile = 2);

        await sink.WriteAsync(Everything, [.. Enumerable.Range(0, 5).Select(i => Masked(Record(_tenantA, n: i)))], CancellationToken.None);

        var counts = new List<int>();
        foreach (var file in Files())
        {
            counts.Add((await ReadAsync(file)).Rows.Count);
        }

        counts.OrderBy(c => c).Should().Equal(1, 2, 2);
    }

    [Theory]
    [InlineData(ParquetCompression.Snappy, "SNAPPY")]
    [InlineData(ParquetCompression.Zstd, "ZSTD")]
    [InlineData(ParquetCompression.Gzip, "GZIP")]
    [InlineData(ParquetCompression.None, "UNCOMPRESSED")]
    public async Task The_configured_compression_is_used_and_the_file_still_reads(ParquetCompression compression, string codec)
    {
        await Sink(o => o.Compression = compression).WriteAsync(Everything, [Masked(Record(_tenantA))], CancellationToken.None);

        var (rows, reader) = await ReadAsync(Files().Single());
        await using (reader)
        {
            rows.Should().ContainSingle();
            reader.Metadata!.RowGroups[0].Columns[0].MetaData!.Codec.ToString().Should().Be(codec);
        }
    }

    [Fact]
    public async Task Setting_a_dataset_up_publishes_its_json_schema_and_is_repeatable()
    {
        var sink = Sink();

        await sink.EnsureDatasetAsync(Everything, CancellationToken.None);
        await sink.EnsureDatasetAsync(Everything, CancellationToken.None);

        var path = Path.Combine(_root, "analytics", "_schemas", "everything_v1.schema.json");
        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be(AnalyticsDatasetSchema.ToJsonSchema(Everything));
    }

    [Fact]
    public async Task A_field_named_like_a_system_column_is_refused_permanently()
    {
        var dataset = new AnalyticsDatasetDescriptor("clash", 1, "qa", [new("event_id", AnalyticsFieldType.Guid)]);

        var act = () => Sink().EnsureDatasetAsync(dataset, CancellationToken.None);

        (await act.Should().ThrowAsync<AnalyticsSinkException>().WithMessage("*reserved*")).Which.IsTransient.Should().BeFalse();
        Files("*").Should().BeEmpty();
    }

    [Fact]
    public async Task A_storage_failure_is_transient_and_a_bad_value_is_permanent()
    {
        var broken = new ParquetAnalyticsSink(new FailingStorage(new IOException("disk full")), Options.Create(new ParquetSinkOptions()));
        var bad = Masked(Record(_tenantA)) with { };
        var wrongType = new AnalyticsRecord(bad.EventId, "everything", 1, _tenantA, bad.OccurredAt, new Dictionary<string, object?>(bad.Values) { ["a_long"] = 5 });

        var io = () => broken.WriteAsync(Everything, [bad], CancellationToken.None);
        var type = () => Sink().WriteAsync(Everything, [wrongType], CancellationToken.None);

        (await io.Should().ThrowAsync<AnalyticsSinkException>()).Which.IsTransient.Should().BeTrue();
        (await type.Should().ThrowAsync<AnalyticsSinkException>()).Which.IsTransient.Should().BeFalse();
        Files().Should().BeEmpty("a failed batch leaves no half-written file");
    }

    [Fact]
    public async Task A_missing_required_value_is_a_permanent_failure()
    {
        var record = Masked(Record(_tenantA));
        var values = new Dictionary<string, object?>(record.Values);
        values.Remove("a_text");

        var act = () => Sink().WriteAsync(Everything, [record with { Values = values }], CancellationToken.None);

        (await act.Should().ThrowAsync<AnalyticsSinkException>().WithMessage("*a_text*")).Which.IsTransient.Should().BeFalse();
    }

    [Fact]
    public async Task An_empty_batch_writes_nothing()
    {
        await Sink().WriteAsync(Everything, [], CancellationToken.None);

        Files("*").Should().BeEmpty();
    }

    [Fact]
    public async Task The_sink_passes_the_conformance_suite()
    {
        var sink = Sink();
        var dataset = new AnalyticsDatasetDescriptor("sample", 1, "qa", [new("label", AnalyticsFieldType.String)]);

        var report = await AnalyticsSinkConformance.RunAsync(
            sink,
            dataset,
            async (tenant, name) =>
            {
                var folder = Path.Combine(_root, "analytics", $"{name}_v1", $"tenant={tenant:D}");
                var total = 0L;
                if (Directory.Exists(folder))
                {
                    foreach (var file in Directory.EnumerateFiles(folder, "*.parquet", SearchOption.AllDirectories))
                    {
                        total += (await ReadAsync(file)).Rows.Count;
                    }
                }

                return total;
            },
            (id, label) => new AnalyticsRecord(id, "sample", 1, Guid.NewGuid(), DateTimeOffset.UtcNow, new Dictionary<string, object?> { ["label"] = label }));

        report.Passed.Should().BeTrue(report.ToString());
    }

    [Fact]
    public async Task The_dispatcher_delivers_masked_batches_end_to_end()
    {
        var catalog = new AnalyticsDatasetCatalog();
        catalog.Add(Everything);
        var dispatcher = new AnalyticsDispatcher(catalog, [Sink()], [], new FakeProtector());

        var result = await dispatcher.DispatchAsync([Record(_tenantA), Record(_tenantA)]);

        result.IsComplete.Should().BeTrue();
        var (rows, reader) = await ReadAsync(Files().Single());
        await using (reader)
        {
            rows.Should().HaveCount(2).And.OnlyContain(r => (string)r["an_email"]! == "hash:a@b.c" && !r.ContainsKey("a_secret"));
        }
    }

    [Theory]
    [InlineData("analytics", 100, 38, 9, true)]
    [InlineData("a/b-c_d.e", 1, 10, 0, true)]
    [InlineData("../escape", 100, 38, 9, false)]
    [InlineData("has space", 100, 38, 9, false)]
    [InlineData("analytics", 0, 38, 9, false)]
    [InlineData("analytics", 100, 39, 9, false)]
    [InlineData("analytics", 100, 10, 11, false)]
    public void Options_are_validated_at_startup(string root, int rows, int precision, int scale, bool valid)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Analytics:Parquet:RootPath"] = root,
            ["Analytics:Parquet:MaxRowsPerFile"] = rows.ToString(CultureInfo.InvariantCulture),
            ["Analytics:Parquet:DecimalPrecision"] = precision.ToString(CultureInfo.InvariantCulture),
            ["Analytics:Parquet:DecimalScale"] = scale.ToString(CultureInfo.InvariantCulture),
        }).Build();
        var services = new ServiceCollection();
        services.AddModulusAnalytics(a => a.AddParquet(configuration));
        using var provider = services.BuildServiceProvider();

        var read = () => provider.GetRequiredService<IOptions<ParquetSinkOptions>>().Value;

        if (valid)
        {
            read().RootPath.Should().Be(root);
        }
        else
        {
            read.Should().Throw<OptionsValidationException>();
        }
    }

    [Fact]
    public void Registration_adds_one_parquet_sink()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFileStorage>(new LocalFileStorage(Options.Create(new StorageOptions { BasePath = _root })));
        var configuration = new ConfigurationBuilder().Build();
        services.AddModulusAnalytics(a => a.AddParquet(configuration).AddParquet(configuration));
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IAnalyticsSink>().Should().ContainSingle().Which.Name.Should().Be("parquet");
    }
}
