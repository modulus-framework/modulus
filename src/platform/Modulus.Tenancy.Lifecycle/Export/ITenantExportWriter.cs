namespace Modulus.Tenancy.Lifecycle;

/// <summary>
/// Where a handler writes what it exports. The writer produces, for each dataset, a JSON array and a CSV file of the same
/// records, plus any attached files, all in one ZIP archive described by a manifest.
/// </summary>
public interface ITenantExportWriter
{
    /// <summary>
    /// Writes the records of the current dataset as <c>{dataset}.json</c> (an array) and <c>{dataset}.csv</c> (a header row, then
    /// one row per record). Columns are the record's public, simple-typed properties. Call once per dataset.
    /// </summary>
    Task WriteRecordsAsync<T>(IAsyncEnumerable<T> records, CancellationToken ct = default);

    /// <summary>
    /// Copies a stored file (an upload, a document) into the archive under <c>files/{storagePath}</c>. The path is relative: a
    /// rooted path or a <c>..</c> segment is refused.
    /// </summary>
    Task AttachFileAsync(string storagePath, CancellationToken ct = default);
}
