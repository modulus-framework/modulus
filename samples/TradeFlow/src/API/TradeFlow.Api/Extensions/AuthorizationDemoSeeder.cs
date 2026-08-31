using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Management;
using Modulus.Authorization.Organization;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Api.Extensions;

/// <summary>
/// Seed the EF-backed authorization store with the demo world used by the
/// FEATURE-TOUR scenarios (org hierarchy, placements, grants, and a baseline
/// delegation). This is what makes the <c>/api/authorization</c> management API
/// meaningful out of the box: the delegator actually holds the delegated
/// permission in the store, so the delegation resolver's cap passes and a
/// delegate's <c>HasPermission</c> flips on immediately — no token re-issue.
/// Idempotent; a no-op once the root org unit exists.
/// </summary>
internal static class AuthorizationDemoSeeder
{
    // Org hierarchy mirrors SampleDataSeeder: Acme -> North/South -> warehouses.
    private static readonly Guid AcmeCompanyId = Guid.Parse("aaaa0000-0000-0000-0000-000000000001");
    private static readonly Guid NorthRegionId = Guid.Parse("aaaa0000-0000-0000-0000-000000000011");
    private static readonly Guid SouthRegionId = Guid.Parse("aaaa0000-0000-0000-0000-000000000012");
    private static readonly Guid[] Warehouses =
    [
        Guid.Parse("aaaa0000-0000-0000-0000-000000000101"), // NYC  (North)
        Guid.Parse("aaaa0000-0000-0000-0000-000000000102"), // Boston (North)
        Guid.Parse("aaaa0000-0000-0000-0000-000000000201"), // Miami (South)
        Guid.Parse("aaaa0000-0000-0000-0000-000000000202"), // Atlanta (South)
    ];

    // Demo personas (stable ids, matching SampleDataSeeder): Diana the buyer,
    // Eve the purchasing manager (delegator), Bob the deputy (delegate).
    private static readonly Guid DianaBuyerId = Guid.Parse("d1a00000-0000-0000-0000-000000000001");
    private static readonly Guid EvePurchasingMgrId = Guid.Parse("e1e00000-0000-0000-0000-000000000001");
    private static readonly Guid BobBranchMgrId = Guid.Parse("b0b00000-0000-0000-0000-000000000001");

    public static async Task SeedAuthorizationAsync(IServiceScope scope)
    {
        ILogger logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            var hierarchy = scope.ServiceProvider.GetRequiredService<EfOrgHierarchy>();
            var placements = scope.ServiceProvider.GetRequiredService<EfOrgPlacementStore>();
            var grants = scope.ServiceProvider.GetRequiredService<EfPermissionGrantStore>();
            var delegations = scope.ServiceProvider.GetRequiredService<EfDelegationStore>();

            if (hierarchy.Contains(AcmeCompanyId))
            {
                logger.LogInformation("Authorization store already seeded, skipping");
                return;
            }

            // ── Org hierarchy ─────────────────────────────────────────────────
            await hierarchy.AddUnitAsync(AcmeCompanyId, [], CancellationToken.None);
            await hierarchy.AddUnitAsync(NorthRegionId, [AcmeCompanyId], CancellationToken.None);
            await hierarchy.AddUnitAsync(SouthRegionId, [AcmeCompanyId], CancellationToken.None);
            foreach (var warehouse in Warehouses)
            {
                var region = warehouse == Warehouses[0] || warehouse == Warehouses[1]
                    ? NorthRegionId : SouthRegionId;
                await hierarchy.AddUnitAsync(warehouse, [region], CancellationToken.None);
            }

            await placements.PlaceAsync(
                DianaBuyerId, NorthRegionId, OrgScopeMode.UnitAndDescendants, CancellationToken.None);
            await placements.PlaceAsync(
                EvePurchasingMgrId, NorthRegionId, OrgScopeMode.UnitAndDescendants, CancellationToken.None);
            await placements.PlaceAsync(
                BobBranchMgrId, Warehouses[0], OrgScopeMode.UnitOnly, CancellationToken.None);

            // ── Grants ────────────────────────────────────────────────────────
            // The Admin role may administer authorization data and manage users;
            // Diana can create requisitions; Eve (the delegator) holds the
            // approval authority she later lends to Bob.
            await grants.GrantToRoleAsync(
                "Admin",
                [
                    AuthorizationManagementExtensions.ManagePermission,
                    AppPermissions.IdentityAdmin,
                    AppPermissions.IdentityUserViewAll,
                    AppPermissions.IdentityUserManageAll,
                ],
                CancellationToken.None);
            await grants.GrantToUserAsync(DianaBuyerId, ["purchasing:create"], CancellationToken.None);
            await grants.GrantToUserAsync(EvePurchasingMgrId, ["purchasing:approve"], CancellationToken.None);

            // ── Baseline delegation (the FEATURE-TOUR Scenario 2) ─────────────
            // Eve lends her approval authority to Bob for the next two weeks.
            // Time-bounded and revocable; capped by Eve's own grant above.
            var now = DateTimeOffset.UtcNow;
            await delegations.DelegateAsync(
                fromUserId: EvePurchasingMgrId,
                fromRoles: ["Admin"],
                toUserId: BobBranchMgrId,
                permissions: ["purchasing:approve"],
                notBefore: now,
                notAfter: now.AddDays(14),
                CancellationToken.None);

            logger.LogInformation("Seeded authorization store: org units, placements, grants, and a demo delegation");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed authorization data");
            // Don't throw — authorization seeding is optional, the API can still start.
        }
    }
}
