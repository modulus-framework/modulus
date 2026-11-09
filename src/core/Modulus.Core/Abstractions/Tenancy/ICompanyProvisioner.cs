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

    /// <summary>
    /// For a company that already exists: adds the baseline permissions the company's roles lack, and returns what it added as
    /// <c>role:permission</c>. It only adds. A permission the role already has a row for (allowed or denied) is left as it is, so a deliberate
    /// change is never undone; a permission that was removed outright cannot be told from one never granted and is added back. Run it when a release
    /// adds a permission to the baseline (a generator adds <c>tenant-data:export</c> or <c>webhooks:manage</c> to it): a company created earlier
    /// kept the grants it had. The default does nothing.
    /// </summary>
    Task<IReadOnlyList<string>> TopUpAsync(TenantInfo company, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
}
