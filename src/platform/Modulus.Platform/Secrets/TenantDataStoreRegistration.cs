namespace Modulus.Secrets;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions.Tenancy;

/// <summary>Registration of the tenant database resolver.</summary>
public static class TenantDataStoreRegistration
{
    /// <summary>
    /// Registers <see cref="ITenantDatabaseResolver"/> with the <c>TenantDataStores</c> settings. Requires
    /// <see cref="SecretsExtensions.AddModulusSecrets"/> and an <see cref="ITenantDataStoreCatalog"/> (the EF catalog is
    /// added by <c>AddEfCoreTenantStore</c>).
    /// </summary>
    public static IServiceCollection AddModulusTenantDatabases(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TenantDataStoreOptions>()
            .Bind(configuration.GetSection(TenantDataStoreOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ITenantDatabaseResolver, TenantDatabaseResolver>();
        services.TryAddScoped<Modulus.Events.Abstractions.IIntegrationEventHandler<TenantDataStoreChanged>, TenantDataStoreChangedHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TenantSecretBackendCheck>());
        return services;
    }
}
