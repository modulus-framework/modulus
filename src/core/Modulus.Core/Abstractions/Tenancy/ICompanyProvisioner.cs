namespace Modulus.Core.Abstractions;

/// <summary>
/// Gives a company its starting grants from the configured provisioning baseline (D-10). The baseline is configuration,
/// never a copy of another company's live grants, and it never includes platform-level or Critical permissions. Registered by the
/// authorization store; without it a company starts with no grants (fail-closed).
/// </summary>
public interface ICompanyProvisioner
{
    /// <summary>
    /// Applies the baseline to <paramref name="company"/> and returns the roles that received grants in this call. A role that already holds
    /// grants in the company is left alone, so the call is safe to repeat and never overwrites a deliberate change. Throws when the configured
    /// baseline is invalid.
    /// </summary>
    Task<IReadOnlyList<string>> ProvisionAsync(TenantInfo company, CancellationToken ct = default);
}
