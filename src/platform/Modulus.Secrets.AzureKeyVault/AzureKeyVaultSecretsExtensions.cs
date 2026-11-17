namespace Modulus.Secrets.AzureKeyVault;

using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Secrets;

/// <summary>Registers Azure Key Vault as the secret provider of Modulus (read path).</summary>
public static class AzureKeyVaultSecretsExtensions
{
    /// <summary>
    /// Registers the cached <c>ISecretStore</c> backed by Azure Key Vault, authenticated with <c>DefaultAzureCredential</c>,
    /// plus the Key Vault health check. Settings are bound from <c>Secrets:AzureKeyVault</c> and validated at startup.
    /// </summary>
    public static IServiceCollection AddAzureKeyVaultSecretStore(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModulusSecrets(configuration);
        services.AddOptions<AzureKeyVaultOptions>()
            .Bind(configuration.GetSection(AzureKeyVaultOptions.SectionName))
            .Validate(o => o.Validate() is null, "Secrets:AzureKeyVault settings are invalid.")
            .ValidateOnStart();

        services.TryAddSingleton(sp => CreateClient(sp.GetRequiredService<IOptions<AzureKeyVaultOptions>>().Value));
        services.Replace(ServiceDescriptor.Singleton<ISecretBackend>(sp => new AzureKeyVaultSecretStore(
            sp.GetRequiredService<SecretClient>(),
            sp.GetRequiredService<IOptions<AzureKeyVaultOptions>>().Value)));
        services.AddSingleton<IModuleHealthCheck>(sp => new AzureKeyVaultHealthCheck(sp.GetRequiredService<SecretClient>()));
        return services;
    }

    internal static SecretClient CreateClient(AzureKeyVaultOptions options)
    {
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId) ? null : options.ManagedIdentityClientId,
        });
        var clientOptions = new SecretClientOptions();
        clientOptions.Retry.NetworkTimeout = options.RequestTimeout;
        return new SecretClient(new Uri(options.VaultUri), credential, clientOptions);
    }
}
