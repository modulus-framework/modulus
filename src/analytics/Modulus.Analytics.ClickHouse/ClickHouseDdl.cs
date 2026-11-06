namespace Modulus.Analytics.ClickHouse;

using System.Text;
using System.Text.RegularExpressions;

/// <summary>Builds and checks the ClickHouse table for a dataset version. Pure text: nothing here talks to a server.</summary>
internal static partial class ClickHouseDdl
{
    internal const string TenantColumn = "tenant_id";
    internal const string EventColumn = "event_id";
    internal const string OccurredColumn = "occurred_at";
    internal const string IngestedColumn = "ingested_at";

    internal static readonly string[] SystemColumns = [TenantColumn, EventColumn, OccurredColumn, IngestedColumn];

    internal sealed record Column(string Name, string Type, string? Comment = null);

    [GeneratedRegex("^[a-z0-9_]*$")]
    internal static partial Regex PrefixPattern();

    internal static string TableName(ClickHouseSinkOptions options, AnalyticsDatasetDescriptor dataset)
        => $"{options.TablePrefix}{dataset.Name}_v{dataset.Version}";

    /// <summary>The field names that clash with the sink's own columns.</summary>
    internal static IEnumerable<string> Collisions(AnalyticsDatasetDescriptor dataset)
        => dataset.Fields.Select(f => f.Name).Where(n => SystemColumns.Contains(n, StringComparer.Ordinal));

    /// <summary>The fields that reach ClickHouse: everything the masking policy does not drop.</summary>
    internal static IReadOnlyList<AnalyticsFieldDescriptor> DeliveredFields(AnalyticsDatasetDescriptor dataset)
        => [.. dataset.Fields.Where(f => f.EffectiveMasking != AnalyticsMaskingAction.Drop)];

    /// <summary>The columns the table must have, in order, as the server reports their types.</summary>
    internal static IReadOnlyList<Column> Columns(AnalyticsDatasetDescriptor dataset, ClickHouseSinkOptions options)
    {
        var columns = new List<Column>
        {
            new(TenantColumn, "UUID"),
            new(EventColumn, "UUID"),
            new(OccurredColumn, "DateTime64(3, 'UTC')"),
            new(IngestedColumn, "DateTime64(3, 'UTC')"),
        };

        foreach (var field in DeliveredFields(dataset))
        {
            var type = field.EffectiveMasking == AnalyticsMaskingAction.Hash ? "String" : TypeOf(field.Type, options);
            columns.Add(new Column(field.Name, field.IsNullable ? $"Nullable({type})" : type, $"{field.Classification}; {field.EffectiveMasking}"));
        }

        return columns;
    }

    internal static string TypeOf(AnalyticsFieldType type, ClickHouseSinkOptions options) => type switch
    {
        AnalyticsFieldType.Int64 => "Int64",
        AnalyticsFieldType.Decimal => $"Decimal({options.DecimalPrecision}, {options.DecimalScale})",
        AnalyticsFieldType.Double => "Float64",
        AnalyticsFieldType.Boolean => "Bool",
        AnalyticsFieldType.Timestamp => "DateTime64(6, 'UTC')",
        AnalyticsFieldType.Date => "Date",
        AnalyticsFieldType.Guid => "UUID",
        _ => "String",
    };

    internal static string CreateTable(AnalyticsDatasetDescriptor dataset, ClickHouseSinkOptions options)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE IF NOT EXISTS ").Append(Quote(TableName(options, dataset))).AppendLine().AppendLine("(");
        var columns = Columns(dataset, options);
        for (var i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            sb.Append("    ").Append(Quote(c.Name)).Append(' ').Append(c.Type);
            if (c.Name == IngestedColumn)
            {
                sb.Append(" DEFAULT now64(3)");
            }

            if (c.Comment is not null)
            {
                sb.Append(" COMMENT ").Append(Literal(c.Comment));
            }

            sb.AppendLine(i < columns.Count - 1 ? "," : string.Empty);
        }

        sb.AppendLine(")");
        sb.Append("ENGINE = ReplacingMergeTree(").Append(Quote(IngestedColumn)).AppendLine(")");
        sb.Append("PARTITION BY toYYYYMM(").Append(Quote(OccurredColumn)).AppendLine(")");
        sb.Append("ORDER BY (").Append(Quote(TenantColumn)).Append(", toDate(").Append(Quote(OccurredColumn)).Append("), ").Append(Quote(EventColumn)).AppendLine(")");
        if (dataset.RetentionDays is { } days)
        {
            sb.Append("TTL toDateTime(").Append(Quote(IngestedColumn)).Append(") + INTERVAL ").Append(days).AppendLine(" DAY");
        }

        sb.Append("COMMENT ").Append(Literal($"owner={dataset.Owner}; dataset={dataset.Key}"));
        return sb.ToString();
    }

    /// <summary>Compares the columns the server reports with the ones the dataset needs. Returns one line per difference; empty means they match.</summary>
    internal static IReadOnlyList<string> Diff(IReadOnlyList<Column> expected, IReadOnlyDictionary<string, string> actual)
    {
        var problems = new List<string>();
        foreach (var column in expected)
        {
            if (!actual.TryGetValue(column.Name, out var type))
            {
                problems.Add($"missing column {column.Name} ({column.Type})");
            }
            else if (!string.Equals(type, column.Type, StringComparison.Ordinal))
            {
                problems.Add($"column {column.Name} is {type}, expected {column.Type}");
            }
        }

        problems.AddRange(actual.Keys.Where(n => expected.All(c => c.Name != n)).Select(n => $"unexpected column {n}"));
        return problems;
    }

    internal static string Quote(string identifier) => "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`";

    internal static string Literal(string text)
        => "'" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";
}
