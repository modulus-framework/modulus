namespace Modulus.Analytics.ClickHouse;

using global::ClickHouse.Driver;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

/// <summary>Registers the ClickHouse sink.</summary>
public static class ClickHouseAnalyticsExtensions
{
    /// <summary>
    /// Adds ClickHouse as an analytics sink (settings from <c>Analytics:ClickHouse</c>, <see cref="ClickHouseSinkOptions"/>).
    /// </summary>
    /// <param name="builder">The analytics builder.</param>
    /// <param name="configuration">Supplies the <c>Analytics:ClickHouse</c> section.</param>
    /// <param name="configure">Adjusts the options after binding.</param>
    public static AnalyticsBuilder AddClickHouse(this AnalyticsBuilder builder, IConfiguration configuration, Action<ClickHouseSinkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.AddOptions<ClickHouseSinkOptions>()
            .Bind(configuration.GetSection(ClickHouseSinkOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "Analytics:ClickHouse:ConnectionString is required.")
            .Validate(o => ClickHouseDdl.PrefixPattern().IsMatch(o.TablePrefix), "Analytics:ClickHouse:TablePrefix may contain only a-z, 0-9 and _.")
            .Validate(o => o.DecimalPrecision is >= 1 and <= 76 && o.DecimalScale >= 0 && o.DecimalScale <= o.DecimalPrecision,
                "Analytics:ClickHouse decimal precision must be 1-76 and the scale between 0 and the precision.")
            .Validate(o => o.InsertBatchSize > 0, "Analytics:ClickHouse:InsertBatchSize must be positive.")
            .ValidateOnStart();
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        builder.Services.TryAddSingleton(sp => new ClickHouseClient(sp.GetRequiredService<IOptions<ClickHouseSinkOptions>>().Value.ConnectionString));
        return builder.AddSink<ClickHouseAnalyticsSink>();
    }
}
