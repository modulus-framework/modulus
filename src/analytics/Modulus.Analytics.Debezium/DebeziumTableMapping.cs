namespace Modulus.Analytics.Debezium;

/// <summary>
/// How one source table's Debezium change events become records of an analytics dataset.
/// </summary>
/// <param name="Table">The source table, as Debezium names it in <c>source.table</c>.</param>
/// <param name="Dataset">The dataset version the rows fill. Its field names are the source column names.</param>
/// <param name="KeyColumn">The column that identifies the row. Its value makes the record id, so a change to the same row replaces it in the warehouse.</param>
/// <param name="TenantColumn">The column that holds the company id (a GUID).</param>
/// <param name="DeletedField">
/// The Boolean field of the dataset that records a hard delete: it is <see langword="true"/> on a delete event and <see langword="false"/> otherwise. Without it,
/// a delete is not visible in the warehouse. <see langword="null"/> means the dataset keeps no delete marker.
/// </param>
/// <param name="Schema">The source schema, when more than one schema has a table with this name. <see langword="null"/> matches any schema.</param>
public sealed record DebeziumTableMapping(
    string Table,
    AnalyticsDatasetDescriptor Dataset,
    string KeyColumn,
    string TenantColumn = "tenant_id",
    string? DeletedField = null,
    string? Schema = null);

/// <summary>What reading a batch of change events produced.</summary>
/// <param name="Records">The records, in the order the events were read.</param>
/// <param name="Ignored">The events that need no record: a table without a mapping, or an operation other than create, read, update or delete.</param>
/// <param name="Problems">One sentence per event that could not become a record, naming its position in the batch.</param>
public sealed record DebeziumReadResult(IReadOnlyList<AnalyticsRecord> Records, int Ignored, IReadOnlyList<string> Problems);
