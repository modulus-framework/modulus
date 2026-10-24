using Microsoft.Extensions.Options;
using Modulus.Authorization;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Security;
using Modulus.Core.Null;

namespace Modulus.Authorization.EntityFrameworkCore;

/// <summary>
/// The starting grants of every new company, read from <c>CompanyProvisioning:Grants</c>: role name to the permissions the role holds
/// inside the company. Absent means no baseline (a new company has no grants). The baseline is configuration only; it is never copied
/// from another company's live grants.
/// </summary>
public sealed class CompanyProvisioningOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "CompanyProvisioning";

    /// <summary>Role name to permission names, applied to each new company.</summary>
    public Dictionary<string, string[]> Grants { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Applies <see cref="CompanyProvisioningOptions"/> to a company. A baseline that names an unregistered permission, a Critical permission,
/// a wildcard, or a reserved <c>integration:</c> role is refused with <see cref="InvalidOperationException"/>, so a misconfigured baseline
/// stops the caller (the company API returns an error; the startup seed fails the boot) instead of granting something unintended.
/// A role that already holds grants in the company is left alone, so the call is safe to repeat.
/// </summary>
internal sealed class CompanyProvisioner(
    EfPermissionGrantStore grants,
    IPermissionRegistry registry,
    IEnumerable<IPermissionRegistration> declarations,
    ICurrentTenant tenant,
    IServiceProvider services,
    IOptions<CompanyProvisioningOptions> options) : ICompanyProvisioner
{
    /// <inheritdoc />
    public async Task ProvisionAsync(TenantInfo company, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(company);
        var baseline = Validate(options.Value.Grants);
        if (baseline.Count == 0)
            return;

        using var _ = tenant.Change(company);
        foreach (var (role, permissions) in baseline)
        {
            if ((await grants.GetGrantsForHolderAsync(GrantHolderType.Role, role, ct)).Count > 0)
                continue;
            await grants.GrantToRoleAsync(role, permissions, ct);
        }

        (services.GetService(typeof(ISecurityAuditLog)) as ISecurityAuditLog ?? NullSecurityAuditLog.Instance).Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Authorization,
            Action = "company.provisioned",
            TenantId = company.TenantId,
            Target = $"tenant:{company.TenantId:D}",
            Outcome = SecurityAuditOutcomes.Success,
        });
    }

    private IReadOnlyDictionary<string, string[]> Validate(Dictionary<string, string[]> configured)
    {
        // Module declarations reach the registry when the host starts, which is after the first-boot seeds. Apply them here too: redeclaring
        // a permission with the same sensitivity is allowed, so the later start-up apply changes nothing.
        foreach (var declaration in declarations)
            declaration.Apply(registry);

        var known = registry.GetAll().ToDictionary(p => p.Permission, StringComparer.Ordinal);
        foreach (var (role, permissions) in configured)
        {
            if (string.IsNullOrWhiteSpace(role) || role.StartsWith("integration:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{CompanyProvisioningOptions.SectionName}:Grants names a role that cannot hold a company baseline: '{role}'.");
            if (permissions.Length == 0)
                throw new InvalidOperationException($"{CompanyProvisioningOptions.SectionName}:Grants:{role} lists no permissions.");

            foreach (var permission in permissions)
            {
                if (permission.Contains('*', StringComparison.Ordinal))
                    throw new InvalidOperationException($"The company baseline for role '{role}' names the wildcard '{permission}'. Wildcards are not allowed.");
                if (!known.TryGetValue(permission, out var definition))
                    throw new InvalidOperationException($"The company baseline for role '{role}' names '{permission}', which is not a registered permission.");
                if (definition.Sensitivity == PermissionSensitivity.Critical)
                    throw new InvalidOperationException($"The company baseline for role '{role}' names '{permission}', which is Critical. A company baseline never grants Critical permissions.");
            }
        }

        return configured;
    }
}
