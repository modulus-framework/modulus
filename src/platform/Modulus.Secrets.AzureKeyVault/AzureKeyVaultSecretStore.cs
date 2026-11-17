namespace Modulus.Secrets.AzureKeyVault;

using System.Net;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Core.Abstractions.Secrets;
using Modulus.Secrets;

/// <summary>
/// The runtime read path over Azure Key Vault. Read-only: the provider does not implement <see cref="ISecretWriter"/>, because
/// Key Vault has no atomic check-and-set on the integer versions the writer contract needs. Provision secrets with Azure tooling.
/// Callers reach it only through the cached <see cref="ISecretStore"/>.
/// </summary>
internal sealed class AzureKeyVaultSecretStore(SecretClient client, AzureKeyVaultOptions options) : ISecretBackend
{
    /// <inheritdoc />
    public async Task<SecretValue> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!string.IsNullOrEmpty(options.PathPrefix) && !reference.Path.StartsWith(options.PathPrefix + "/", StringComparison.Ordinal))
            throw new ArgumentException("The secret is outside the configured Azure Key Vault namespace.", nameof(reference));

        var name = AzureKeyVaultNaming.ToSecretName(reference);
        try
        {
            var response = await client.GetSecretAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false);
            var secret = response.Value;
            var fields = AzureKeyVaultNaming.ReadFields(secret.Value ?? string.Empty);
            var version = AzureKeyVaultNaming.ToVersionNumber(secret.Properties.UpdatedOn, secret.Properties.CreatedOn);
            return new SecretValue(fields, version);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            throw new SecretNotFoundException("The secret was not found.");
        }
        catch (RequestFailedException ex) when (IsUnavailable(ex.Status))
        {
            throw new SecretStoreUnavailableException("Azure Key Vault is unreachable or throttling requests.", ex);
        }
        catch (RequestFailedException ex) when (ex.Status is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("Azure Key Vault denied access to a secret. Check the identity's access policy or RBAC role.");
        }
        catch (RequestFailedException ex)
        {
            throw new InvalidOperationException($"Azure Key Vault returned an unexpected status {ex.Status}.", ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SecretStoreUnavailableException("Azure Key Vault did not answer in time.");
        }
    }

    /// <summary>No response (status 0), throttling, or a server error: a bounded stale value may serve in the meantime.</summary>
    internal static bool IsUnavailable(int status) => status <= 0 || status == 429 || status >= 500;
}
