namespace Modulus.Analytics.Parquet;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>Registers the Parquet sink.</summary>
public static class ParquetAnalyticsExtensions
{
    /// <summary>
    /// Adds Parquet files as an analytics sink (settings from <c>Analytics:Parquet</c>, <see cref="ParquetSinkOptions"/>). The app must
    /// register an unscoped <c>IFileStorage</c> (local disk, S3 or Azure Blob).
    /// </summary>
    /// <param name="builder">The analytics builder.</param>
    /// <param name="configuration">Supplies the <c>Analytics:Parquet</c> section.</param>
    /// <param name="configure">Adjusts the options after binding.</param>
    public static AnalyticsBuilder AddParquet(this AnalyticsBuilder builder, IConfiguration configuration, Action<ParquetSinkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.AddOptions<ParquetSinkOptions>()
            .Bind(configuration.GetSection(ParquetSinkOptions.SectionName))
            .Validate(o => ParquetLayout.RootPattern().IsMatch(o.RootPath) && !o.RootPath.Contains("..", StringComparison.Ordinal),
                "Analytics:Parquet:RootPath may contain only letters, digits, _, -, . and /, and no '..'.")
            .Validate(o => o.MaxRowsPerFile > 0, "Analytics:Parquet:MaxRowsPerFile must be positive.")
            .Validate(o => o.DecimalPrecision is >= 1 and <= 38 && o.DecimalScale >= 0 && o.DecimalScale <= o.DecimalPrecision,
                "Analytics:Parquet decimal precision must be 1-38 and the scale between 0 and the precision.")
            .ValidateOnStart();
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        return builder.AddSink<ParquetAnalyticsSink>();
    }
}
