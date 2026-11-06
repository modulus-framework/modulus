namespace Modulus.Analytics.Superset;

using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

/// <summary>Registers the Superset guest-token helper for embedded, per-company dashboards.</summary>
public static partial class SupersetServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ISupersetGuestTokens"/> (settings from <c>Analytics:Superset</c>, <see cref="SupersetOptions"/>), validated at startup.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration that holds the section.</param>
    public static IServiceCollection AddModulusSupersetGuestTokens(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SupersetOptions>()
            .Bind(configuration.GetSection(SupersetOptions.SectionName))
            .Validate(o => o.BaseUrl is { IsAbsoluteUri: true, Scheme: "https" or "http" },
                "Analytics:Superset:BaseUrl must be an absolute https URL (http only for development).")
            .Validate(o => TenantColumnPattern().IsMatch(o.TenantColumn ?? string.Empty),
                "Analytics:Superset:TenantColumn must be a lower-case identifier.")
            .Validate(o => o.Timeout > TimeSpan.Zero, "Analytics:Superset:Timeout must be positive.")
            .ValidateOnStart();

        services.AddHttpClient(SupersetGuestTokenService.HttpClientName)
            .ConfigureHttpClient((sp, client) => client.Timeout = sp.GetRequiredService<IOptions<SupersetOptions>>().Value.Timeout);

        services.AddSingleton<ISupersetGuestTokens, SupersetGuestTokenService>();
        return services;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex TenantColumnPattern();
}
