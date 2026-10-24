namespace Modulus.Core.Abstractions;

/// <summary>
/// Gives a newly created company its starting grants from the configured provisioning baseline (D-10). The baseline is configuration,
/// never a copy of another company's live grants, and it never includes platform-level or Critical permissions. Registered by the
/// authorization store; without it a company starts with no grants (fail-closed).
/// </summary>
public interface ICompanyProvisioner
{
    /// <summary>Applies the baseline to <paramref name="company"/>. Throws when the configured baseline is invalid.</summary>
    Task ProvisionAsync(TenantInfo company, CancellationToken ct = default);
}
