namespace Modulus.Analytics.ClickHouse;

/// <summary>ClickHouse sink settings, bound from the <c>Analytics:ClickHouse</c> configuration section.</summary>
public sealed class ClickHouseSinkOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Analytics:ClickHouse";

    /// <summary>
    /// The ClickHouse HTTP connection string, for example <c>Host=clickhouse;Port=8123;Protocol=http;Database=analytics;Username=modulus;Password=...</c>.
    /// The database must exist. Keep the password in a secret store, not in the settings file.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>A prefix for every table name: lower case letters, digits and underscores. Empty by default.</summary>
    public string TablePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Whether the sink creates missing tables. Turn it off when database administrators own the DDL: the sink then only checks that
    /// the table exists and matches the dataset contract. Defaults to <see langword="true"/>.
    /// </summary>
    public bool CreateTables { get; set; } = true;

    /// <summary>Total digits of a <c>Decimal</c> field's column. Defaults to 38.</summary>
    public int DecimalPrecision { get; set; } = 38;

    /// <summary>Digits after the point in a <c>Decimal</c> field's column. Defaults to 9.</summary>
    public int DecimalScale { get; set; } = 9;

    /// <summary>Rows per insert request. Defaults to 10000.</summary>
    public int InsertBatchSize { get; set; } = 10_000;
}
