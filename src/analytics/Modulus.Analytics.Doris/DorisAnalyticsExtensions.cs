namespace Modulus.Analytics.Doris;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the Doris sink.</summary>
public static class DorisAnalyticsExtensions
{
    /// <summary>
    /// Adds Apache Doris as an analytics sink (settings from <c>Analytics:Doris</c>, <see cref="DorisSinkOptions"/>), validated at startup.
    /// </summary>
    /// <param name="builder">The analytics builder.</param>
    /// <param name="configuration">Supplies the <c>Analytics:Doris</c> section.</param>
    /// <param name="configure">Adjusts the options after binding.</param>
    public static AnalyticsBuilder AddDoris(this AnalyticsBuilder builder, IConfiguration configuration, Action<DorisSinkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.AddOptions<DorisSinkOptions>()
            .Bind(configuration.GetSection(DorisSinkOptions.SectionName))
            .Validate(o => o.FeHttpUrl is { IsAbsoluteUri: true, Scheme: "https" or "http" },
                "Analytics:Doris:FeHttpUrl must be an absolute http or https URL.")
            .Validate(o => DorisSinkOptions.IsIdentifier(o.Database), "Analytics:Doris:Database must be a lower-case identifier.")
            .Validate(o => DorisSinkOptions.IsPrefix(o.TablePrefix), "Analytics:Doris:TablePrefix may contain only a-z, 0-9 and _.")
            .Validate(o => o.Timeout > TimeSpan.Zero, "Analytics:Doris:Timeout must be positive.")
            .ValidateOnStart();
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        return builder.AddSink<DorisAnalyticsSink>();
    }
}
