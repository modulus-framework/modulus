namespace Modulus.Analytics.ClickHouse;

using global::ClickHouse.Driver;
using Microsoft.Extensions.Options;

/// <summary>
/// Writes analytics datasets to ClickHouse. Each dataset <em>version</em> gets its own table, so a new version never needs an
/// <c>ALTER</c>. Rows are keyed by tenant: the table is a <c>ReplacingMergeTree</c> ordered by
/// (<c>tenant_id</c>, day, <c>event_id</c>), so a record sent twice collapses to one row when ClickHouse merges parts.
/// </summary>
/// <remarks>
/// Merging is eventual: until it happens a duplicate can be visible, so queries that need exact counts use <c>FINAL</c> (or
/// <c>argMax</c>). A column's comment records its classification and masking, so the warehouse documents itself.
/// </remarks>
public sealed class ClickHouseAnalyticsSink(ClickHouseClient client, IOptions<ClickHouseSinkOptions> options) : IAnalyticsReconcilableSink
{
    /// <inheritdoc />
    public string Name => "clickhouse";

    /// <inheritdoc />
    public async Task EnsureDatasetAsync(AnalyticsDatasetDescriptor dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var settings = options.Value;

        var clashes = ClickHouseDdl.Collisions(dataset).ToList();
        if (clashes.Count > 0)
        {
            throw new AnalyticsSinkException(
                $"Dataset '{dataset.Key}' uses reserved ClickHouse column name(s): {string.Join(", ", clashes)}.", isTransient: false);
        }

        var table = ClickHouseDdl.TableName(settings, dataset);
        try
        {
            if (settings.CreateTables)
            {
                await client.ExecuteNonQueryAsync(ClickHouseDdl.CreateTable(dataset, settings), cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            var actual = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var reader = await client.ExecuteReaderAsync(
                $"SELECT name, type FROM system.columns WHERE database = currentDatabase() AND table = {ClickHouseDdl.Literal(table)}",
                cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    actual[reader.GetString(0)] = reader.GetString(1);
                }
            }

            if (actual.Count == 0)
            {
                throw new AnalyticsSinkException(
                    $"ClickHouse table '{table}' does not exist and the sink is not allowed to create it (Analytics:ClickHouse:CreateTables is off).",
                    isTransient: false);
            }

            var problems = ClickHouseDdl.Diff(ClickHouseDdl.Columns(dataset, settings), actual);
            if (problems.Count > 0)
            {
                throw new AnalyticsSinkException(
                    $"ClickHouse table '{table}' does not match dataset '{dataset.Key}': {string.Join("; ", problems)}. A dataset version is immutable: publish a new version instead of changing this one.",
                    isTransient: false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ClickHouseErrors.Translate($"set up table '{table}'", ex);
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
        var table = ClickHouseDdl.TableName(settings, dataset);
        try
        {
            var fields = ClickHouseDdl.DeliveredFields(dataset);
            var columns = new List<string> { ClickHouseDdl.TenantColumn, ClickHouseDdl.EventColumn, ClickHouseDdl.OccurredColumn };
            columns.AddRange(fields.Select(f => f.Name));

            var rows = records.Select(r => BuildRow(fields, r)).ToList();
            await client.InsertBinaryAsync(
                ClickHouseDdl.Quote(table),
                columns,
                rows,
                new InsertOptions { BatchSize = settings.InsertBatchSize },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ClickHouseErrors.Translate($"write {records.Count} row(s) to '{table}'", ex);
        }
    }

    /// <inheritdoc />
    /// <remarks>Counts with <c>FINAL</c>, so records not yet collapsed by a merge count once.</remarks>
    public async Task<long> CountAsync(AnalyticsDatasetDescriptor dataset, Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var table = ClickHouseDdl.TableName(options.Value, dataset);
        try
        {
            var count = await client.ExecuteScalarAsync(
                $"SELECT count() FROM {ClickHouseDdl.Quote(table)} FINAL WHERE {ClickHouseDdl.Quote(ClickHouseDdl.TenantColumn)} = {ClickHouseDdl.Literal(tenantId.ToString("D"))}",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return Convert.ToInt64(count, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ClickHouseErrors.Translate($"count the rows of '{table}'", ex);
        }
    }

    private static object?[] BuildRow(IReadOnlyList<AnalyticsFieldDescriptor> fields, AnalyticsRecord record)
    {
        var row = new object?[3 + fields.Count];
        row[0] = record.TenantId;
        row[1] = record.EventId;
        row[2] = record.OccurredAt.UtcDateTime;
        for (var i = 0; i < fields.Count; i++)
        {
            record.Values.TryGetValue(fields[i].Name, out var value);
            row[3 + i] = ToClickHouse(fields[i], value);
        }

        return row;
    }

    private static object? ToClickHouse(AnalyticsFieldDescriptor field, object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (field.EffectiveMasking == AnalyticsMaskingAction.Hash)
        {
            return (string)value;
        }

        return field.Type switch
        {
            AnalyticsFieldType.Timestamp => ((DateTimeOffset)value).UtcDateTime,
            AnalyticsFieldType.Date => ((DateOnly)value).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            _ => value,
        };
    }
}
