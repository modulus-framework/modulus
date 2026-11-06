namespace Modulus.Analytics.Parquet;

using System.Globalization;
using global::Parquet;
using global::Parquet.Schema;

/// <summary>Builds the Parquet schema for a dataset and writes a batch of records into it.</summary>
internal static class ParquetColumns
{
    internal static ParquetSchema Build(IReadOnlyList<AnalyticsFieldDescriptor> fields, ParquetSinkOptions options)
    {
        var columns = new List<DataField>
        {
            new DataField(ParquetLayout.TenantColumn, typeof(string), isNullable: false),
            new DataField(ParquetLayout.EventColumn, typeof(string), isNullable: false),
            new DateTimeDataField(ParquetLayout.OccurredColumn, DateTimeFormat.DateAndTimeMicros, isNullable: false),
        };
        columns.AddRange(fields.Select(f => ToField(f, options)));
        return new ParquetSchema(columns.ToArray<Field>());
    }

    private static DataField ToField(AnalyticsFieldDescriptor field, ParquetSinkOptions options)
    {
        var nullable = field.IsNullable;
        if (field.EffectiveMasking == AnalyticsMaskingAction.Hash)
        {
            return new DataField(field.Name, typeof(string), isNullable: nullable);
        }

        return field.Type switch
        {
            AnalyticsFieldType.Int64 => new DataField(field.Name, typeof(long), isNullable: nullable),
            AnalyticsFieldType.Decimal => new DecimalDataField(field.Name, options.DecimalPrecision, options.DecimalScale, isNullable: nullable),
            AnalyticsFieldType.Double => new DataField(field.Name, typeof(double), isNullable: nullable),
            AnalyticsFieldType.Boolean => new DataField(field.Name, typeof(bool), isNullable: nullable),
            AnalyticsFieldType.Timestamp => new DateTimeDataField(field.Name, DateTimeFormat.DateAndTimeMicros, isNullable: nullable),
            AnalyticsFieldType.Date => new DataField(field.Name, typeof(DateOnly), isNullable: nullable),
            _ => new DataField(field.Name, typeof(string), isNullable: nullable),
        };
    }

    internal static async Task WriteFileAsync(
        Stream output,
        ParquetSchema schema,
        IReadOnlyList<AnalyticsFieldDescriptor> fields,
        IReadOnlyList<AnalyticsRecord> rows,
        ParquetSinkOptions settings,
        CancellationToken cancellationToken)
    {
        var parquetOptions = new ParquetOptions
        {
            CompressionMethod = settings.Compression switch
            {
                ParquetCompression.Zstd => CompressionMethod.Zstd,
                ParquetCompression.Gzip => CompressionMethod.Gzip,
                ParquetCompression.None => CompressionMethod.None,
                _ => CompressionMethod.Snappy,
            },
            UseDateOnlyTypeForDates = true,
        };

        await using var writer = await ParquetWriter.CreateAsync(schema, output, parquetOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var group = writer.CreateRowGroup();
        var data = schema.DataFields;

        await group.WriteAsync(data[0], (IReadOnlyCollection<string>)[.. rows.Select(r => r.TenantId.ToString("D"))]).ConfigureAwait(false);
        await group.WriteAsync(data[1], (IReadOnlyCollection<string>)[.. rows.Select(r => r.EventId.ToString("D"))]).ConfigureAwait(false);
        await group.WriteAsync<DateTime>(data[2], new ReadOnlyMemory<DateTime>([.. rows.Select(r => r.OccurredAt.UtcDateTime)]), cancellationToken: cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var values = rows.Select(r => Prepare(field, r.Values.TryGetValue(field.Name, out var v) ? v : null)).ToList();
            await WriteColumnAsync(group, data[3 + i], field, values, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Gives a value the exact CLR type its column is written with (a GUID becomes text, a timestamp becomes a UTC <see cref="DateTime"/>).</summary>
    private static object? Prepare(AnalyticsFieldDescriptor field, object? value)
    {
        if (value is null)
        {
            if (!field.IsNullable)
            {
                throw new InvalidOperationException($"Field '{field.Name}' is required but a record has no value for it.");
            }

            return null;
        }

        if (field.EffectiveMasking == AnalyticsMaskingAction.Hash)
        {
            return (string)value;
        }

        return field.Type switch
        {
            AnalyticsFieldType.Timestamp => ((DateTimeOffset)value).UtcDateTime,
            AnalyticsFieldType.Guid => ((Guid)value).ToString("D", CultureInfo.InvariantCulture),
            AnalyticsFieldType.String => (string)value,
            AnalyticsFieldType.Int64 => (long)value,
            AnalyticsFieldType.Decimal => (decimal)value,
            AnalyticsFieldType.Double => (double)value,
            AnalyticsFieldType.Boolean => (bool)value,
            _ => (DateOnly)value,
        };
    }

    private static Task WriteColumnAsync(
        ParquetRowGroupWriter group, DataField column, AnalyticsFieldDescriptor field, List<object?> values, CancellationToken ct)
    {
        if (field.EffectiveMasking == AnalyticsMaskingAction.Hash)
        {
            return WriteText(group, column, values, ct);
        }

        return field.Type switch
        {
            AnalyticsFieldType.Int64 => WriteValue<long>(group, column, values, field.IsNullable, ct),
            AnalyticsFieldType.Decimal => WriteValue<decimal>(group, column, values, field.IsNullable, ct),
            AnalyticsFieldType.Double => WriteValue<double>(group, column, values, field.IsNullable, ct),
            AnalyticsFieldType.Boolean => WriteValue<bool>(group, column, values, field.IsNullable, ct),
            AnalyticsFieldType.Timestamp => WriteValue<DateTime>(group, column, values, field.IsNullable, ct),
            AnalyticsFieldType.Date => WriteValue<DateOnly>(group, column, values, field.IsNullable, ct),
            _ => WriteText(group, column, values, ct),
        };
    }

    private static Task WriteText(ParquetRowGroupWriter group, DataField column, List<object?> values, CancellationToken ct)
        => group.WriteAsync(column, (IReadOnlyCollection<string>)[.. values.Select(v => (string)v!)]);

    private static Task WriteValue<T>(ParquetRowGroupWriter group, DataField column, List<object?> values, bool nullable, CancellationToken ct)
        where T : struct
        => nullable
            ? group.WriteAsync<T>(column, new ReadOnlyMemory<T?>([.. values.Select(v => v is null ? (T?)null : (T)v)]), cancellationToken: ct)
            : group.WriteAsync<T>(column, new ReadOnlyMemory<T>([.. values.Select(v => (T)v!)]), cancellationToken: ct);
}
