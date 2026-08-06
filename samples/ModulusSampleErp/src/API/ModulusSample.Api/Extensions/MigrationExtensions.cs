using System;
using System.Threading.Tasks;
using ModulusSample.Modules.Identity.Infrastructure.Database;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
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
            await ApplyMigrationAsync<IdentityDbContext>(scope, logger);

            logger.LogInformation("All database migrations applied successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while applying migrations");
            throw new ApplicationException("An error occurred while applying migrations", ex);
        }
    }

    /// <summary>
    /// Seeds data only.
    /// </summary>
    internal static async Task ApplySeeding(this IApplicationBuilder app)
    {
        using IServiceScope scope = app.ApplicationServices.CreateScope();
        ILogger logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            await SeedIdentityModule(scope);
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

    private static async Task ApplyMigrationAsync<TDbContext>(IServiceScope scope, ILogger logger)
        where TDbContext : DbContext
    {
        TDbContext context = scope.ServiceProvider.GetRequiredService<TDbContext>();
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
