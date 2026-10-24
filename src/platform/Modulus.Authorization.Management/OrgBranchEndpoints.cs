using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Core.Abstractions;

namespace Modulus.Authorization.Management;

/// <summary>An organizational unit the caller may select as the branch of a request.</summary>
public sealed record OrgBranchChoice(Guid Id, string Code, string Name, string Kind);

/// <summary>
/// The branches a caller may select (decision 14): the open organizational units inside the caller's data scope. The list is a convenience
/// for the picker. Each request still carries its branch through <c>BranchContextMiddleware</c>, which checks the same scope, so a branch
/// outside it is refused whatever the client sends.
/// </summary>
public static class OrgBranchEndpoints
{
    /// <summary>
    /// The units <paramref name="scope"/> allows, open ones only, by code. A caller whose scope is unrestricted sees every open unit.
    /// </summary>
    public static IReadOnlyList<OrgBranchChoice> SelectableBranches(IEnumerable<OrgUnitProfile> units, ICurrentDataScope scope)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(scope);
        return [.. units
            .Where(u => !u.IsClosed && (scope.IsUnrestricted || scope.OrgUnitIds.Contains(u.UnitId)))
            .OrderBy(u => u.Code, StringComparer.OrdinalIgnoreCase)
            .Select(u => new OrgBranchChoice(u.UnitId, u.Code, u.Name, u.Kind))];
    }

    /// <summary>Maps <c>GET {prefix}</c> (default <c>/api/org/branches</c>) for any signed-in caller.</summary>
    public static RouteGroupBuilder MapModulusOrgBranches(this IEndpointRouteBuilder endpoints, string prefix = "/api/org/branches")
    {
        var group = endpoints.MapGroup(prefix).RequireAuthorization();

        group.MapGet("/", async (EfOrganizationProfileStore profiles, ICurrentDataScope scope, CancellationToken ct) =>
            Results.Ok(SelectableBranches(await profiles.ListUnitsAsync(ct), scope)));

        return group;
    }
}
