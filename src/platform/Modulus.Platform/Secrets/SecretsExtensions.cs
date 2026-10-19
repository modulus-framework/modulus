namespace Modulus.Secrets;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Secrets;

/// <summary>Registration of the secret layer.</summary>
public static class SecretsExtensions
{
    /// <summary>
    /// Registers <see cref="ISecretStore"/> as the cached view of the provider's <see cref="ISecretBackend"/>, and
    /// <see cref="ISecretCache"/> for invalidation. With no provider registered the development configuration backend is
    /// used. Binds and validates <c>Secrets</c> at startup.
    /// </summary>
    public static IServiceCollection AddModulusSecrets(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SecretsOptions>()
            .Bind(configuration.GetSection(SecretsOptions.SectionName))
            .Validate(o => o.Validate() is null, "Secrets options are invalid.")
            .ValidateOnStart();

        services.TryAddSingleton<ISecretBackend>(sp => new ConfigurationSecretStore(sp.GetRequiredService<IConfiguration>()));
        services.TryAddSingleton<CachedSecretStore>();
        services.TryAddSingleton<ISecretStore>(sp => sp.GetRequiredService<CachedSecretStore>());
        services.TryAddSingleton<ISecretCache>(sp => sp.GetRequiredService<CachedSecretStore>());
        return services;
    }
}
