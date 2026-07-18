namespace Modulus.AspNetCore.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers the secrets guard rail. It scans the effective configuration at startup
/// and, in Development/Staging by default, fails fast when a connection string, key,
/// or other sensitive value is committed to <c>appsettings*.json</c> rather than
/// supplied by environment variables, User Secrets, or a vault. Configuration lives
/// under the <c>SecretsGuard</c> section (see <see cref="SecretsGuardOptions"/>).
/// </summary>
public static class SecretsGuardExtensions
{
    /// <summary>
    /// Binds <see cref="SecretsGuardOptions"/> and registers the startup guard. The
    /// guard is a no-op outside its configured environments, so it is safe to wire
    /// unconditionally; production deployments are excluded by default to avoid a
    /// false positive ever blocking a boot.
    /// </summary>
    public static IServiceCollection AddModulusSecretsGuard(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<SecretsGuardOptions>? configure = null)
    {
        services.AddOptions<SecretsGuardOptions>()
            .Bind(configuration.GetSection(SecretsGuardOptions.SectionName))
            .ValidateOnStart();
        if (configure is not null)
            services.Configure(configure);

        services.AddHostedService<SecretsGuardHostedService>();
        return services;
    }
}
