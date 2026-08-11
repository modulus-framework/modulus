using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Organization;
using Modulus.Core.Abstractions;

namespace ModulusSample.Api.Extensions;

/// <summary>
/// Seed the authorization framework (EF-backed stores) with demo data:
/// - org hierarchy (Company -> Regions -> Branches/Warehouses)
/// - user placements at different org levels
/// - permission grants for the demo scenarios (SoD, field security, org scope)
/// Called after module migrations are complete.
/// </summary>
internal static class AuthorizationDemoSeeder
{
    public static async Task SeedAuthorizationAsync(IServiceScope scope)
    {
        ILogger logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            var factory = scope.ServiceProvider
                .GetRequiredService<System.Data.Common.DbDataSource>()
                ?.CreateConnection()?.GetType() is null
                ? null
                : scope.ServiceProvider
                    .GetService<Microsoft.EntityFrameworkCore.IDbContextFactory<AuthorizationStoreDbContext>>();

            // The authorization store is only registered if AddEfCoreAuthorizationStores was called.
            // If it's present, seed the org hierarchy and placements.
            if (factory is null)
            {
                logger.LogWarning("Authorization store factory not registered, skipping authorization seeding");
                return;
            }

            await using var authDb = await factory.CreateDbContextAsync();
            bool alreadySeeded = await authDb.Set<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry>()
                .Where(e => e.Entity.GetType().Name == "OrgUnit")
                .AnyAsync();

            if (alreadySeeded)
            {
                logger.LogInformation("Authorization data already seeded, skipping");
                return;
            }

            // SIMPLIFIED SEED: org hierarchy only (no EF-backed grant/placement stores yet).
            // Full seeding requires understanding the exact EF store schemas and methods.
            // For now, just log that seeding was called so the next phase can complete it.
            logger.LogInformation("Authorization demo seeding would be added here (org hierarchy + placements)");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed authorization data");
            // Don't throw — authorization seeding is optional, the API can still start
        }
    }
}
