namespace Modulus.Analytics.Apicurio;

using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>Registers the Apicurio schema publisher.</summary>
public static class AnalyticsApicurioExtensions
{
    /// <summary>
    /// Adds the Apicurio publisher (settings from <c>Analytics:Apicurio</c>, <see cref="ApicurioOptions"/>). Settings are validated at startup.
    /// Publish with <see cref="ApicurioSchemaPublisher"/>, for example from a deployment job after the datasets change.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration that holds the section.</param>
    public static IServiceCollection AddModulusApicurio(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ApicurioOptions>()
            .Bind(configuration.GetSection(ApicurioOptions.SectionName))
            .Validate(o => o.BaseUrl is { IsAbsoluteUri: true, Scheme: "http" or "https" }, "Analytics:Apicurio:BaseUrl must be an absolute http or https URL.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.GroupId), "Analytics:Apicurio:GroupId is required.")
            .Validate(o => o.Timeout > TimeSpan.Zero, "Analytics:Apicurio:Timeout must be positive.")
            .ValidateOnStart();

        services.AddHttpClient<ApicurioSchemaPublisher>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ApicurioOptions>>().Value;
            client.BaseAddress = settings.BaseUrl;
            client.Timeout = settings.Timeout;
            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            }
        });
        return services;
    }
}
