namespace Modulus.Analytics.DuckDb;

using DuckDB.NET.Data;

/// <summary>
/// Counts a tenant's distinct records in a dataset's Parquet files, read in place by DuckDB. The lake's files are immutable and an
/// identical batch writes the same file name, so a record is counted once however many times it was delivered. Use it to reconcile
/// the lake with the queue, or to check a lake that a Spark or Trino job reads, without a warehouse running.
/// </summary>
/// <remarks>
/// The reader works on a folder DuckDB can read: a local disk or a mounted volume. It does not read object storage directly; for
/// S3 or Azure, mount the bucket, or run the count inside the platform's own query engine.
/// </remarks>
public sealed class DuckDbLakeReconciler
{
    /// <summary>The folder of one dataset version under the lake root, <c>{root}/{name}_v{version}</c>, as the Parquet sink lays it out.</summary>
    /// <param name="lakeRoot">The lake root folder (the sink's base path joined with its root path).</param>
    /// <param name="dataset">The dataset version.</param>
    public static string DatasetDirectory(string lakeRoot, AnalyticsDatasetDescriptor dataset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lakeRoot);
        ArgumentNullException.ThrowIfNull(dataset);
        return Path.Combine(lakeRoot, $"{dataset.Name}_v{dataset.Version}");
    }

    /// <summary>
    /// Counts the distinct records of one tenant in a dataset version's folder. Returns 0 when the folder or the tenant has no files.
    /// </summary>
    /// <param name="datasetDirectory">The dataset version's folder, from <see cref="DatasetDirectory"/>.</param>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    public async Task<long> CountDistinctRecordsAsync(string datasetDirectory, Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetDirectory);
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant id is required; the empty id is not a tenant.", nameof(tenantId));
        }

        if (!Directory.Exists(datasetDirectory))
        {
            return 0;
        }

        // Only this tenant's partitions are opened, and the tenant_id column is checked too, so a misplaced file cannot inflate the count.
        var tenantFolder = Path.Combine(datasetDirectory, $"tenant={tenantId:D}");
        if (!Directory.Exists(tenantFolder))
        {
            return 0;
        }

        var pattern = Path.Combine(tenantFolder, "dt=*", "*.parquet").Replace('\\', '/').Replace("'", "''", StringComparison.Ordinal);
        await using var connection = new DuckDBConnection("DataSource=:memory:");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(DISTINCT event_id) FROM read_parquet('{pattern}') WHERE tenant_id = $tenant";
        command.Parameters.Add(new DuckDBParameter("tenant", tenantId.ToString("D")));
        try
        {
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is null or DBNull ? 0 : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (DuckDBException ex) when (ex.Message.Contains("No files found", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
    }
}
