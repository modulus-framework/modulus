namespace Modulus.Webhooks;

using Microsoft.AspNetCore.DataProtection;

/// <summary>
/// Encrypts signing secrets at rest with ASP.NET Data Protection (the secret must be recoverable to sign, so it cannot be
/// hashed). With more than one replica, or across restarts, the key ring must be persisted and shared, or stored secrets
/// can no longer be read.
/// </summary>
internal sealed class WebhookSecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Modulus.Webhooks.Secrets.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
