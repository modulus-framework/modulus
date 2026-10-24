using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Tenancy;

namespace Modulus.MultiTenancy;

/// <summary>One company the caller may act in, as the company switcher lists it.</summary>
/// <param name="TenantId">The company's id: send it as the company header to act in it.</param>
/// <param name="TenantSlug">The company's short name.</param>
/// <param name="DisplayName">The company's display name, if set.</param>
/// <param name="Status">Trial, Active or Suspended (a suspended company can be read but not changed).</param>
/// <param name="Current">Whether the request in hand acts in this company.</param>
public sealed record CompanyChoice(Guid TenantId, string TenantSlug, string? DisplayName, string Status, bool Current);

/// <summary>
/// The company switcher's list (decision 29): the companies the signed-in person holds an active membership in. Membership is the only
/// source: a company the person does not belong to is never listed, and a closed company does not resolve, so it is not listed either.
/// Choosing a company is the client's job (it sends the company header on the next request); the request is then checked against
/// membership again by <see cref="TenantMiddleware"/>, so this list is a convenience and not an access decision.
/// </summary>
public static class CompanySwitcherEndpoints
{
    /// <summary>Maps <c>GET {path}</c>, which lists the caller's companies. Requires a signed-in user.</summary>
    public static RouteHandlerBuilder MapModulusCompanySwitcher(this IEndpointRouteBuilder endpoints, string path = "/api/account/companies")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapGet(path, async (
            ICurrentUser user,
            ITenantMembershipStore memberships,
            ITenantStore companies,
            ICurrentTenant current,
            CancellationToken ct) =>
        {
            if (user.UserId is not { } userId)
                return Results.Unauthorized();

            var choices = new List<CompanyChoice>();
            foreach (var tenantId in await memberships.ListTenantIdsAsync(userId, ct))
            {
                var company = await companies.FindByIdAsync(tenantId, ct);
                // A closed company is not offered, even if a stale membership still names it.
                if (company is null || company.Status == TenantStatus.Closed)
                    continue;

                choices.Add(new CompanyChoice(
                    company.TenantId,
                    company.TenantSlug,
                    company.DisplayName,
                    company.Status.ToString(),
                    current.TenantId == company.TenantId));
            }

            return Results.Ok(choices.OrderBy(c => c.TenantSlug, StringComparer.Ordinal).ToList());
        }).RequireAuthorization();
    }
}
