namespace Modulus.EntityFrameworkCore.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Data.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using Modulus.Events.Abstractions;

public static class EFCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers a module DbContext + EfRepository + EfReadRepository.
    /// configure: opts => opts.UseSqlServer(...) or UseNpgsql(...).
    /// </summary>
    public static IServiceCollection AddModuleDatabase<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configure)
        where TContext : ModuleDbContext
    {
        services.AddDbContext<TContext>(configure);

        // Also register as DbContext so TransactionBehavior (which resolves
        // GetServices<DbContext>()) discovers every module context and wraps
        // them all in a transaction — not just the first one.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());

        // Default no-op outbox. Replaced by EfOutboxWriter when AddOutbox
        // is called.  TryAdd so the first registration wins and AddOutbox
        // can override via Replace.
        services.TryAddScoped<IIntegrationEventOutbox, NullIntegrationEventOutbox>();

        services.AddScoped<IUnitOfWork>(
            sp => sp.GetRequiredService<TContext>());
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        return services;
    }

}