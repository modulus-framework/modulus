namespace Modulus.Analytics.Parquet;

/// <summary>The compression applied to each Parquet file.</summary>
public enum ParquetCompression
{
    /// <summary>Snappy: fast, and read by every engine. The default.</summary>
    Snappy = 0,

    /// <summary>Zstandard: smaller files than Snappy for a little more CPU. Read by current Spark, Trino and DuckDB.</summary>
    Zstd = 1,

    /// <summary>Gzip: widely supported, slower.</summary>
    Gzip = 2,

    /// <summary>No compression.</summary>
    None = 3,
}

/// <summary>Parquet sink settings, bound from the <c>Analytics:Parquet</c> configuration section.</summary>
public sealed class ParquetSinkOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Analytics:Parquet";

    /// <summary>The folder (or key prefix) every dataset is written under. Defaults to <c>analytics</c>.</summary>
    public string RootPath { get; set; } = "analytics";

    /// <summary>The compression of each file. Defaults to Snappy.</summary>
    public ParquetCompression Compression { get; set; } = ParquetCompression.Snappy;

    /// <summary>
    /// The most rows in one file. A bigger batch is split into several files. Keep files well under the storage upload limit
    /// (<c>Storage:MaxUploadBytes</c>, 100 MB by default). Defaults to 100000.
    /// </summary>
    public int MaxRowsPerFile { get; set; } = 100_000;

    /// <summary>Total digits of a <c>Decimal</c> field's column. Defaults to 38.</summary>
    public int DecimalPrecision { get; set; } = 38;

    /// <summary>Digits after the point in a <c>Decimal</c> field's column. Defaults to 9.</summary>
    public int DecimalScale { get; set; } = 9;
}
