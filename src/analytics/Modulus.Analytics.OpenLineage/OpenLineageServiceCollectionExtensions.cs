namespace Modulus.Analytics.OpenLineage;

using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Analytics;

/// <summary>Registers the OpenLineage emitter of the analytics backfill runs.</summary>
public static class OpenLineageServiceCollectionExtensions
{
    /// <summary>
    /// Reports backfill runs to an OpenLineage endpoint (settings from <c>Analytics:OpenLineage</c>, <see cref="OpenLineageOptions"/>). Replaces the
    /// default lineage emitter of <c>AddModulusAnalytics</c>. Settings are validated at startup when the emitter is enabled. Incremental syncs are not reported.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration that holds the section.</param>
    public static IServiceCollection AddModulusOpenLineage(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OpenLineageOptions>()
            .Bind(configuration.GetSection(OpenLineageOptions.SectionName))
            .Validate(o => !o.Enabled || o.Url is { IsAbsoluteUri: true, Scheme: "http" or "https" },
                "Analytics:OpenLineage:Url must be an absolute http or https URL when OpenLineage is enabled.")
            .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.Namespace) && !string.IsNullOrWhiteSpace(o.WarehouseNamespace),
                "Analytics:OpenLineage:Namespace and WarehouseNamespace must be set when OpenLineage is enabled.")
            .Validate(o => o.Timeout > TimeSpan.Zero, "Analytics:OpenLineage:Timeout must be positive.")
            .ValidateOnStart();

        services.AddHttpClient(OpenLineageEmitter.HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                var settings = sp.GetRequiredService<IOptions<OpenLineageOptions>>().Value;
                client.Timeout = settings.Timeout;
                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
                }
            });

        services.Replace(ServiceDescriptor.Singleton<IAnalyticsLineageEmitter, OpenLineageEmitter>());
        return services;
    }
}
