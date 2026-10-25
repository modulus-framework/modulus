using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Modulus.Core.Abstractions;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>The answer of <see cref="ParentCompanyAuthority.CheckAsync"/>: whether the caller may act, and the rule that decided it.</summary>
/// <param name="Allowed">True when every check passed.</param>
/// <param name="Reason">A code naming the rule that refused (or <c>ALLOWED</c>). Written to the security audit; never shown to the caller.</param>
public sealed record CompanyAuthorityDecision(bool Allowed, string Reason)
{
    /// <summary>The decision when every check passed.</summary>
    public static CompanyAuthorityDecision Allow { get; } = new(true, "ALLOWED");

    /// <summary>A refusal that names its rule.</summary>
    public static CompanyAuthorityDecision Deny(string reason) => new(false, reason);
}

/// <summary>
/// Decides whether a parent company's administrator may manage one of its child companies (decision 6). Every check passes or the
/// request is refused; nothing is cached, so a revoked authority or a removed permission applies on the next request:
/// <list type="number">
/// <item>The child exists and its parent is the company named in the route (a valid relationship).</item>
/// <item>Neither company is suspended or closed (read-only companies accept no change).</item>
/// <item>The caller holds <see cref="ChildrenManagePermission"/> <i>in the parent</i> (the parent permission).</item>
/// <item>An active <see cref="CompanyAuthorityGrant"/> names this caller, this parent, this child and the operation. A
/// <see cref="CompanyOperation.ManageMembers"/> grant never covers a grant operation, and the reverse also holds.</item>
/// </list>
/// The checks run in the host context, so a caller cannot widen them by selecting a company.
/// </summary>
public sealed class ParentCompanyAuthority(
    ITenantStore store,
    ICurrentTenant tenant,
    IAuthorizationService authorization,
    ICompanyAuthorityStore? authorities = null)
{
    /// <summary>The permission a parent administrator must hold in the parent company. Critical: granted only deliberately.</summary>
    public const string ChildrenManagePermission = "tenants:children:manage";

    /// <summary>Checks whether <paramref name="caller"/> may perform <paramref name="operation"/> in <paramref name="childId"/> through <paramref name="parentId"/>.</summary>
    public async Task<CompanyAuthorityDecision> CheckAsync(ClaimsPrincipal caller, Guid parentId, Guid childId, CompanyOperation operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (authorities is null)
            return CompanyAuthorityDecision.Deny("AUTHORITY_STORE_UNAVAILABLE");
        if (UserIdOf(caller) is not { } principalId)
            return CompanyAuthorityDecision.Deny("NO_ACCOUNT");

        TenantInfo? child;
        TenantInfo? parent;
        using (tenant.Change(null))
        {
            child = await store.FindByIdAsync(childId, ct);
            parent = await store.FindByIdAsync(parentId, ct);
        }

        if (child is null)
            return CompanyAuthorityDecision.Deny("CHILD_NOT_FOUND");
        if (parent is null)
            return CompanyAuthorityDecision.Deny("PARENT_NOT_FOUND");
        if (child.ParentId != parentId)
            return CompanyAuthorityDecision.Deny("NOT_A_CHILD");
        if (parent.IsReadOnly || child.IsReadOnly)
            return CompanyAuthorityDecision.Deny("READ_ONLY");

        // The permission is read in the parent, so a grant that exists only in the child cannot stand in for it.
        bool holdsParentPermission;
        using (tenant.Change(parent))
        {
            holdsParentPermission = (await authorization.AuthorizeAsync(caller, ChildrenManagePermission)).Succeeded;
        }

        if (!holdsParentPermission)
            return CompanyAuthorityDecision.Deny("PARENT_PERMISSION_MISSING");
        if (!await authorities.HasActiveGrantAsync(parentId, childId, principalId, operation, ct))
            return CompanyAuthorityDecision.Deny(operation is CompanyOperation.ManageGrants ? "NO_MANAGE_GRANTS_AUTHORITY" : "NO_MANAGE_MEMBERS_AUTHORITY");

        return CompanyAuthorityDecision.Allow;
    }

    /// <summary>The caller's account id (<c>NameIdentifier</c> or <c>sub</c>), or null when the principal is not a user.</summary>
    public static Guid? UserIdOf(ClaimsPrincipal caller)
        => Guid.TryParse(caller.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? caller.FindFirst("sub")?.Value, out var id) ? id : null;
}
