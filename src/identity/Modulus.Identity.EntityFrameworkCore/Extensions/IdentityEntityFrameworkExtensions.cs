namespace Modulus.Identity.EntityFrameworkCore.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;

public static class IdentityEntityFrameworkExtensions
{
    /// <summary>
    /// Registers the Modulus identity DbContext with the specified database provider.
    /// Call after the provider has already been configured (e.g. AddPostgreSQLDatabase).
    /// </summary>
    public static IServiceCollection AddModulusIdentityStore<TContext>(
        this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                       .UseDbContext<TContext>();
            });

        return services;
    }

    /// <summary>
    /// Applies identity and OpenIddict migrations during startup.
    /// </summary>
    public static async Task EnsureIdentityCreatedAsync<TContext>(
        this IServiceProvider sp,
        CancellationToken ct = default)
        where TContext : DbContext
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        await db.Database.MigrateAsync(ct);
    }
}
