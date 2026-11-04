namespace Modulus.Analytics.OpenMetadata;

using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>Registers the OpenMetadata catalog publisher.</summary>
public static class AnalyticsOpenMetadataExtensions
{
    /// <summary>
    /// Adds the OpenMetadata publisher (settings from <c>Analytics:OpenMetadata</c>, <see cref="OpenMetadataOptions"/>). Settings are validated
    /// at startup. Publish with <see cref="OpenMetadataCatalogPublisher"/> from a deployment job after the datasets change.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration that holds the section.</param>
    public static IServiceCollection AddModulusOpenMetadata(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OpenMetadataOptions>()
            .Bind(configuration.GetSection(OpenMetadataOptions.SectionName))
            .Validate(o => o.BaseUrl is { IsAbsoluteUri: true, Scheme: "http" or "https" }, "Analytics:OpenMetadata:BaseUrl must be an absolute http or https URL.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ServiceName) && !string.IsNullOrWhiteSpace(o.DatabaseName) && !string.IsNullOrWhiteSpace(o.SchemaName),
                "Analytics:OpenMetadata ServiceName, DatabaseName and SchemaName are required.")
            .Validate(o => o.Timeout > TimeSpan.Zero, "Analytics:OpenMetadata:Timeout must be positive.")
            .ValidateOnStart();

        services.AddHttpClient<OpenMetadataCatalogPublisher>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<OpenMetadataOptions>>().Value;
            client.BaseAddress = settings.BaseUrl;
            client.Timeout = settings.Timeout;
            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            }
        });
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<OpenMetadataOptions>>().Value);
        return services;
    }
}
