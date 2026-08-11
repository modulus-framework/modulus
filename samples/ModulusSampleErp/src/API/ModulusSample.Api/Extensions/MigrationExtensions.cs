using System;
using System.Linq;
using System.Threading.Tasks;
using ModulusSample.Modules.Identity.Infrastructure.Database;
using ModulusSample.Modules.Settings.Infrastructure.Database;
using ModulusSample.Modules.Tenants.Infrastructure.Database;
using ModulusSample.Modules.Features.Infrastructure.Database;
using ModulusSample.Modules.VirtualFileExplorer.Infrastructure.Database;
using ModulusSample.Modules.Notifications.Infrastructure.Database;
using ModulusSample.Modules.Media.Infrastructure.Database;
using ModulusSample.Modules.Catalog.Infrastructure.Database;
using ModulusSample.Modules.Partners.Infrastructure.Database;
using ModulusSample.Modules.Inventory.Infrastructure.Database;
using ModulusSample.Modules.Sales.Infrastructure.Database;
using ModulusSample.Modules.Purchasing.Infrastructure.Database;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Modulus.Authorization.EntityFrameworkCore.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ModulusSample.Api.Extensions;

internal static class MigrationExtensions
{
    /// <summary>
    /// Applies database migrations only. Does NOT seed data.
    /// </summary>
    internal static async Task ApplyMigrations(this IApplicationBuilder app)
    {
        using IServiceScope scope = app.ApplicationServices.CreateScope();
        ILogger logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            // IdentityDbContext has real EF migrations; the other module contexts
            // are created from their model (none of them have migrations yet).
            await ApplyMigrationOrCreateAsync<IdentityDbContext>(scope, logger);
            await ApplyMigrationOrCreateAsync<SettingsDbContext>(scope, logger);
            await ApplyMigrationOrCreateAsync<TenantsDbContext>(scope, logger);
            await ApplyMigrationOrCreateAsync<FeaturesDbContext>(scope, logger);
            await ApplyMigrationOrCreateAsync<VirtualFileExplorerDbContext>(scope, logger);
            await ApplyMigrationOrCreateAsync<NotificationsDbContext>(scope, logger);
            await ApplyMigrationOrCreateAsync<MediaDbContext>(scope, logger);
            await ApplyMigrationOrCreateAsync<CatalogDbContext>(scope, logger);
            await ApplyMigrationOrCreateAsync<PartnersDbContext>(scope, logger);
            await ApplyMigrationOrCreateAsync<InventoryDbContext>(scope, logger);
            await ApplyMigrationOrCreateAsync<SalesDbContext>(scope, logger);
            await ApplyMigrationOrCreateAsync<PurchasingDbContext>(scope, logger);

            // The authorization store is registered through IDbContextFactory only, so it
            // is deliberately outside the module loop above and brings its own helper.
            await app.ApplicationServices.MigrateAuthorizationStoreAsync(
                ensureCreatedIfNoMigrations: true);
            logger.LogInformation("Ensured schema for AuthorizationStoreDbContext");

            logger.LogInformation("All database migrations applied successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while applying migrations");
            throw new ApplicationException("An error occurred while applying migrations", ex);
        }
    }

    /// <summary>
    /// Seeds data only (identity + sample data).
    /// </summary>
    internal static async Task ApplySeeding(this IApplicationBuilder app)
    {
        using IServiceScope scope = app.ApplicationServices.CreateScope();
        ILogger logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            await SeedIdentityModule(scope);
            await SampleDataSeeder.SeedAsync(scope);
            logger.LogInformation("All data seeding completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding data");
            throw new ApplicationException("An error occurred while seeding data", ex);
        }
    }

    private static async Task SeedIdentityModule(IServiceScope scope)
    {
        IdentityDbContext context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        ILogger logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        string environment = configuration.GetValue<string>("ASPNETCORE_ENVIRONMENT") ?? "Development";

        await IdentityDbContextSeed.SeedAsync(context, logger, environment);
    }

    /// <summary>
    /// Migrates a context when it has real EF migrations; otherwise creates the
    /// database + schema from the model snapshot (EnsureCreated). Mirrors the
    /// framework's DatabaseInitializationMode.MigrateOrCreate default.
    /// </summary>
    private static async Task ApplyMigrationOrCreateAsync<TDbContext>(IServiceScope scope, ILogger logger)
        where TDbContext : DbContext
    {
        TDbContext context = scope.ServiceProvider.GetRequiredService<TDbContext>();

        bool hasMigrations = context.Database.GetMigrations().Any();
        if (!hasMigrations)
        {
            await context.Database.EnsureCreatedAsync();
            logger.LogInformation("Ensured schema for {DbContext} (no EF migrations)", typeof(TDbContext).Name);
            return;
        }

        const int maxRetries = 3;
        int retryCount = 0;

        while (true)
        {
            try
            {
                logger.LogInformation("Applying migrations for {DbContext} (Attempt {Attempt}/{MaxAttempts})",
                    typeof(TDbContext).Name, retryCount + 1, maxRetries);

                await context.Database.MigrateAsync();
                logger.LogInformation("Successfully applied migrations for {DbContext}", typeof(TDbContext).Name);
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == "42P07")
            {
                // Handle "relation already exists" error
                logger.LogWarning(ex, "Table already exists while applying migration for {DbContext}. This is expected in some scenarios.", typeof(TDbContext).Name);
                return; // Success, no need to retry
            }
            catch (NpgsqlException ex) when (ex.IsTransient)
            {
                retryCount++;
                logger.LogWarning(ex, "Transient database error while applying migration for {DbContext}. Attempt {Attempt}/{MaxAttempts}",
                    typeof(TDbContext).Name, retryCount, maxRetries);

                if (retryCount >= maxRetries)
                {
                    logger.LogError(ex, "Failed to apply migrations for {DbContext} after {MaxAttempts} attempts",
                        typeof(TDbContext).Name, maxRetries);
                    throw new InvalidOperationException($"Failed to apply migrations for {typeof(TDbContext).Name} after {maxRetries} attempts", ex);
                }

                // Wait before retrying with exponential backoff
                int delaySeconds = (int)Math.Pow(2, retryCount);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to apply migrations for {DbContext}", typeof(TDbContext).Name);
                throw new InvalidOperationException($"Failed to apply migrations for {typeof(TDbContext).Name}", ex);
            }
        }
    }
}
