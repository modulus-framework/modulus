namespace Modulus.Secrets.AzureKeyVault;

using System.Diagnostics;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Modulus.Core.Abstractions;

/// <summary>
/// Reports the vault's state by listing one secret's properties. Reachable is healthy; throttling or no answer is degraded,
/// because cached secrets keep serving within their stale bound; a denied identity is unhealthy.
/// </summary>
public sealed class AzureKeyVaultHealthCheck(SecretClient client) : IModuleHealthCheck
{
    /// <inheritdoc />
    public async Task<ModuleHealthResult> CheckAsync(CancellationToken ct = default)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            await foreach (var _ in client.GetPropertiesOfSecretsAsync(ct).AsPages(pageSizeHint: 1))
            {
                break;
            }

            return new ModuleHealthResult("AzureKeyVault", HealthStatus.Healthy, "Azure Key Vault is reachable.", clock.Elapsed);
        }
        catch (RequestFailedException ex) when (AzureKeyVaultSecretStore.IsUnavailable(ex.Status))
        {
            return new ModuleHealthResult("AzureKeyVault", HealthStatus.Degraded,
                "Azure Key Vault is unreachable or throttling; cached secrets are served within their stale bound.", clock.Elapsed);
        }
        catch (RequestFailedException ex)
        {
            return new ModuleHealthResult("AzureKeyVault", HealthStatus.Unhealthy,
                $"Azure Key Vault answered with status {ex.Status}. Check the identity's access.", clock.Elapsed,
                new Dictionary<string, object> { ["httpStatus"] = ex.Status });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ModuleHealthResult("AzureKeyVault", HealthStatus.Degraded,
                "Azure Key Vault did not answer in time.", clock.Elapsed);
        }
    }
}
