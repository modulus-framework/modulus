using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Modulus.MultiTenancy.EntityFrameworkCore;

/// <summary>
/// Registration + initialisation for the EF Core-backed tenant store.
/// </summary>
public static class EfTenantStoreExtensions
{
    /// <summary>
    /// Replaces the default <c>NullTenantStore</c> with an
    /// <see cref="EfTenantStore"/> backed by <see cref="TenantStoreDbContext"/>,
    /// and registers <see cref="TenantManager"/> for provisioning. Call
    /// <b>after</b> <c>AddMultiTenancy(...)</c>:
    /// <code>
    /// services.AddMultiTenancy(t => t.UseHeaderResolver());
    /// services.AddEfCoreTenantStore(o => o.UseNpgsql(connectionString));
    /// </code>
    /// The context is registered only as <see cref="TenantStoreDbContext"/> — never
    /// as <see cref="DbContext"/> — so it stays out of the module transaction
    /// fan-out and the module migration loop.
    /// </summary>
    public static IServiceCollection AddEfCoreTenantStore(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
    {
        services.AddDbContext<TenantStoreDbContext>(configure);
        services.AddScoped<TenantManager>();

        // Supersede the NullTenantStore registered by AddMultiTenancy so that
        // ITenantStore resolves to the EF implementation regardless of call order.
        services.RemoveAll<ITenantStore>();
        services.AddScoped<ITenantStore, EfTenantStore>();
        return services;
    }

    /// <summary>
    /// Brings the tenant-store schema up to date on startup. Runs in a fresh scope
    /// through the context's execution strategy so connection resilience applies.
    /// </summary>
    /// <param name="services">The application service provider.</param>
    /// <param name="ensureCreatedIfNoMigrations">
    /// When <see langword="true"/> and no migrations are compiled into the context,
    /// falls back to <c>EnsureCreatedAsync</c> (convenient in Development). Leave
    /// <see langword="false"/> in production so a missing migration fails loudly.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task MigrateTenantStoreAsync(
        this IServiceProvider services,
        bool ensureCreatedIfNoMigrations = false,
        CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            if (!ensureCreatedIfNoMigrations || db.Database.GetMigrations().Any())
                await db.Database.MigrateAsync(ct);
            else
                await db.Database.EnsureCreatedAsync(ct);
        });
    }
}
