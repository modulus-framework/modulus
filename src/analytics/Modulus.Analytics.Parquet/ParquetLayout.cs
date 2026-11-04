namespace Modulus.Analytics.Parquet;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>Where files go. The layout is a contract for the engines that read them, so it changes only with a new dataset version.</summary>
internal static partial class ParquetLayout
{
    internal const string TenantColumn = "tenant_id";
    internal const string EventColumn = "event_id";
    internal const string OccurredColumn = "occurred_at";

    internal static readonly string[] SystemColumns = [TenantColumn, EventColumn, OccurredColumn];

    [GeneratedRegex("^[A-Za-z0-9_./-]*$")]
    internal static partial Regex RootPattern();

    /// <summary>The folder of one dataset version: <c>{root}/{dataset}_v{version}</c>.</summary>
    internal static string DatasetFolder(string root, AnalyticsDatasetDescriptor dataset)
        => Join(root, $"{dataset.Name}_v{dataset.Version}");

    /// <summary>
    /// A partition folder: <c>{dataset}_v{n}/tenant={tenant}/dt={yyyy-MM-dd}</c>. The partition keys are named <c>tenant</c> and
    /// <c>dt</c> so they do not clash with the <c>tenant_id</c> column kept inside each file (Hive engines reject a partition key that is
    /// also a column; Iceberg needs the source column in the file).
    /// </summary>
    internal static string PartitionFolder(string root, AnalyticsDatasetDescriptor dataset, Guid tenant, DateOnly day)
        => Join(DatasetFolder(root, dataset), $"tenant={tenant:D}", $"dt={day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");

    /// <summary>The file path. The name is a hash of the file's record ids, so writing the same records again overwrites the same file.</summary>
    internal static string FilePath(string partitionFolder, IEnumerable<Guid> orderedEventIds)
    {
        var text = string.Join(',', orderedEventIds.Select(i => i.ToString("N")));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Join(partitionFolder, $"part-{Convert.ToHexString(hash, 0, 12).ToLowerInvariant()}.parquet");
    }

    internal static string SchemaPath(string root, AnalyticsDatasetDescriptor dataset)
        => Join(root, "_schemas", $"{dataset.Name}_v{dataset.Version}.schema.json");

    private static string Join(params string[] parts)
        => string.Join('/', parts.Select(p => p.Trim('/')).Where(p => p.Length > 0));
}
