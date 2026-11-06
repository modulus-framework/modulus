namespace Modulus.Analytics.Parquet;

using System.Text;
using global::Parquet;
using global::Parquet.Schema;
using Microsoft.Extensions.Options;
using Modulus.Storage;

/// <summary>
/// Writes analytics datasets as Parquet files through <see cref="IFileStorage"/>, so the same sink serves local disk, S3 and Azure Blob. Files
/// are immutable and laid out as <c>{root}/{dataset}_v{n}/tenant={id}/dt={date}/part-{hash}.parquet</c>, the Hive style that Spark, Trino,
/// DuckDB and Iceberg's <c>add_files</c> read directly. Each file keeps <c>tenant_id</c>, <c>event_id</c> and <c>occurred_at</c> inside, so it
/// is self-describing.
/// </summary>
/// <remarks>
/// <para>
/// A file's name is a hash of its record ids, so delivering the same batch again overwrites the same file. A retried batch that now holds
/// <em>different</em> records writes a second file, and a record can then appear in two files. Parquet is append-only, so readers (or a
/// compaction job, an Iceberg <c>MERGE</c>, or a dbt incremental model) keep one row per (<c>tenant_id</c>, <c>event_id</c>).
/// </para>
/// <para>The storage must not be a tenant-scoped wrapper: the sink writes every tenant's partition itself.</para>
/// </remarks>
public sealed class ParquetAnalyticsSink(IFileStorage storage, IOptions<ParquetSinkOptions> options) : IAnalyticsSink
{
    /// <inheritdoc />
    public string Name => "parquet";

    /// <inheritdoc />
    public async Task EnsureDatasetAsync(AnalyticsDatasetDescriptor dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var clashes = dataset.Fields.Select(f => f.Name).Where(n => ParquetLayout.SystemColumns.Contains(n, StringComparer.Ordinal)).ToList();
        if (clashes.Count > 0)
        {
            throw new AnalyticsSinkException(
                $"Dataset '{dataset.Key}' uses reserved Parquet column name(s): {string.Join(", ", clashes)}.", isTransient: false);
        }

        try
        {
            // The contract sits next to the data, so a reader needs nothing from Modulus to understand the files.
            await using var schema = new MemoryStream(Encoding.UTF8.GetBytes(AnalyticsDatasetSchema.ToJsonSchema(dataset)));
            await storage.UploadAsync(ParquetLayout.SchemaPath(options.Value.RootPath, dataset), schema, "application/schema+json", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ParquetErrors.Translate($"publish the schema of '{dataset.Key}'", ex);
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(AnalyticsDatasetDescriptor dataset, IReadOnlyList<AnalyticsRecord> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return;
        }

        var settings = options.Value;
        var fields = dataset.Fields.Where(f => f.EffectiveMasking != AnalyticsMaskingAction.Drop).ToList();
        var schema = ParquetColumns.Build(fields, settings);
        try
        {
            var partitions = records
                .GroupBy(r => (r.TenantId, Day: DateOnly.FromDateTime(r.OccurredAt.UtcDateTime)))
                .OrderBy(g => g.Key.TenantId).ThenBy(g => g.Key.Day);
            foreach (var partition in partitions)
            {
                var folder = ParquetLayout.PartitionFolder(settings.RootPath, dataset, partition.Key.TenantId, partition.Key.Day);
                var ordered = partition.OrderBy(r => r.EventId).ToList();
                foreach (var chunk in ordered.Chunk(Math.Max(1, settings.MaxRowsPerFile)))
                {
                    await using var file = new MemoryStream();
                    await ParquetColumns.WriteFileAsync(file, schema, fields, chunk, settings, cancellationToken).ConfigureAwait(false);
                    file.Position = 0;
                    await storage.UploadAsync(
                        ParquetLayout.FilePath(folder, chunk.Select(r => r.EventId)), file, "application/vnd.apache.parquet", cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ParquetErrors.Translate($"write {records.Count} row(s) of '{dataset.Key}'", ex);
        }
    }
}
