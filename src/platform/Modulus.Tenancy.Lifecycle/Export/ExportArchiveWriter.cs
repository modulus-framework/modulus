namespace Modulus.Tenancy.Lifecycle.Export;

using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Modulus.Storage;

/// <summary>One file in the archive, as the manifest describes it.</summary>
/// <param name="Name">The entry's path inside the archive.</param>
/// <param name="Bytes">The uncompressed size.</param>
/// <param name="Sha256">The lower-case hex SHA-256 of the contents.</param>
/// <param name="Records">For a dataset file, how many records it holds.</param>
public sealed record ArchiveEntry(string Name, long Bytes, string Sha256, long? Records = null);

/// <summary>The archive's manifest: what the archive contains and for whom.</summary>
public sealed record ArchiveManifest(
    string Format,
    int Version,
    Guid TenantId,
    Guid OperationId,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ArchiveEntry> Entries);

/// <summary>
/// Writes one export archive: per dataset a JSON array and a CSV file, attached files under <c>files/</c>, and a
/// <c>manifest.json</c> last. Only one archive entry is open at a time.
/// </summary>
internal sealed class ExportArchiveWriter(ZipArchive zip, IFileStorage files) : ITenantExportWriter
{
    public const string ManifestFormat = "modulus-tenant-export";
    public const int ManifestVersion = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly ConcurrentDictionary<Type, Column[]> Columns = new();

    private readonly List<ArchiveEntry> _entries = [];
    private string? _dataset;

    /// <summary>The entries written so far.</summary>
    public IReadOnlyList<ArchiveEntry> Entries => _entries;

    /// <summary>Starts a dataset: records written next go to <c>{dataset}.json</c> and <c>{dataset}.csv</c>.</summary>
    public void BeginDataset(string dataset) => _dataset = dataset;

    /// <summary>Ends the current dataset.</summary>
    public void EndDataset() => _dataset = null;

    /// <inheritdoc />
    public async Task WriteRecordsAsync<T>(IAsyncEnumerable<T> records, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        var dataset = _dataset ?? throw new InvalidOperationException("Records can only be written inside a dataset export.");
        var columns = ColumnsOf<T>();

        // The CSV is produced in the same pass as the JSON, but a zip entry can only be written one at a time, so the CSV waits in a
        // temporary file. That file holds ciphertext under a key that exists only in memory for this dataset: no plaintext reaches disk.
        var csvKey = RandomNumberGenerator.GetBytes(ArchiveCrypto.KeyBytes);
        var csvTemp = Path.GetTempFileName();
        try
        {
            long count = 0;
            var jsonEntry = zip.CreateEntry($"{dataset}.json", CompressionLevel.Optimal);
            long jsonBytes;
            string jsonHash;
            await using (var csvFile = File.Create(csvTemp))
            await using (var csvSealed = ArchiveCrypto.CreateEncryptingStream(csvFile, csvKey))
            await using (var csv = new StreamWriter(csvSealed, new UTF8Encoding(false)))
            await using (var jsonOut = new HashingStream(jsonEntry.Open()))
            using (var json = new Utf8JsonWriter(jsonOut))
            {
                json.WriteStartArray();
                await csv.WriteLineAsync(string.Join(',', columns.Select(c => Escape(c.Name))).AsMemory(), ct);
                await foreach (var record in records.WithCancellation(ct))
                {
                    JsonSerializer.Serialize(json, record, Json);
                    await csv.WriteLineAsync(Row(record, columns).AsMemory(), ct);
                    count++;
                }
                json.WriteEndArray();
                await json.FlushAsync(ct);
                await csv.FlushAsync(ct);
                jsonBytes = jsonOut.Bytes;
                jsonHash = jsonOut.Sha256Hex();
            }
            _entries.Add(new ArchiveEntry($"{dataset}.json", jsonBytes, jsonHash, count));

            var csvEntry = zip.CreateEntry($"{dataset}.csv", CompressionLevel.Optimal);
            await using var sink = new HashingStream(csvEntry.Open());
            await using (var source = File.OpenRead(csvTemp))
                await ArchiveCrypto.DecryptAsync(source, sink, csvKey, ct);
            _entries.Add(new ArchiveEntry($"{dataset}.csv", sink.Bytes, sink.Sha256Hex(), count));
        }
        finally
        {
            File.Delete(csvTemp);
        }
    }

    /// <inheritdoc />
    public async Task AttachFileAsync(string storagePath, CancellationToken ct = default)
    {
        ValidateStoragePath(storagePath);
        var name = "files/" + storagePath;
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var sink = new HashingStream(entry.Open());
        await using (var source = await files.DownloadAsync(storagePath, ct))
            await source.CopyToAsync(sink, ct);
        _entries.Add(new ArchiveEntry(name, sink.Bytes, sink.Sha256Hex()));
    }

    /// <summary>Writes <c>manifest.json</c>. Call once, after the last dataset.</summary>
    public void WriteManifest(Guid tenantId, Guid operationId, DateTimeOffset createdAt)
    {
        var manifest = new ArchiveManifest(ManifestFormat, ManifestVersion, tenantId, operationId, createdAt, _entries.ToList());
        using var stream = zip.CreateEntry("manifest.json", CompressionLevel.Optimal).Open();
        JsonSerializer.Serialize(stream, manifest, Json);
    }

    private static void ValidateStoragePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.StartsWith('/') || path.StartsWith('\\'))
            throw new ArgumentException("An attached file path must be relative to the company's storage.", nameof(path));
        foreach (var segment in path.Split('/', '\\'))
        {
            if (segment is "" or "." or "..")
                throw new ArgumentException("An attached file path may not contain empty, '.' or '..' segments.", nameof(path));
        }
    }

    private static Column[] ColumnsOf<T>() => Columns.GetOrAdd(typeof(T), static type => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
        .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
        .Where(p => IsSimple(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType))
        .Select(p => new Column(p))
        .ToArray());

    private static bool IsSimple(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid)
        || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly)
        || type == typeof(TimeSpan);

    private static string Row<T>(T record, Column[] columns) =>
        string.Join(',', columns.Select(c => Escape(Format(c.Property.GetValue(record)))));

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        string text => Guard(text),
        bool flag => flag ? "true" : "false",
        DateTimeOffset at => at.ToString("o", CultureInfo.InvariantCulture),
        DateTime at => at.ToString("o", CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>A text cell that starts like a formula would run in a spreadsheet; a quote in front keeps it as text.</summary>
    private static string Guard(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' ? "'" + text : text;

    private static string Escape(string text) =>
        text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;

    private sealed record Column(PropertyInfo Property)
    {
        public string Name => Property.Name;
    }
}
