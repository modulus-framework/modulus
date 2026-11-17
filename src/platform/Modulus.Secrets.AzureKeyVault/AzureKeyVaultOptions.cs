namespace Modulus.Secrets.AzureKeyVault;

/// <summary>
/// Settings of the Azure Key Vault provider (section <c>Secrets:AzureKeyVault</c>). The runtime identity is resolved by
/// <c>DefaultAzureCredential</c>: a managed identity or workload identity in Azure, the developer's login locally. No
/// secret is stored in these settings.
/// </summary>
public sealed class AzureKeyVaultOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Secrets:AzureKeyVault";

    /// <summary>The vault address, for example <c>https://contoso-prod.vault.azure.net/</c>. Must be https.</summary>
    public string VaultUri { get; set; } = string.Empty;

    /// <summary>
    /// The namespace every secret reference must start with, for example <c>tenant-db</c>. A reference outside it is refused
    /// before any call. Empty means no namespace check.
    /// </summary>
    public string? PathPrefix { get; set; }

    /// <summary>The client id of a user-assigned managed identity to use. Empty uses the default identity chain.</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>The network timeout of one request, before the SDK retries it.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Returns the first problem with these settings, or <c>null</c> when they are valid.</summary>
    public string? Validate()
    {
        if (!Uri.TryCreate(VaultUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return "Secrets:AzureKeyVault:VaultUri must be an absolute https address.";
        if (RequestTimeout <= TimeSpan.Zero)
            return "Secrets:AzureKeyVault:RequestTimeout must be positive.";
        if (PathPrefix is { Length: > 0 } prefix && prefix.Contains('/', StringComparison.Ordinal))
            return "Secrets:AzureKeyVault:PathPrefix must be a single segment.";
        return null;
    }
}
