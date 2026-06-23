namespace Modulus.EntityFrameworkCore.Extensions;

using Microsoft.EntityFrameworkCore;
using Modulus.Data.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;

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
        services.AddScoped<IUnitOfWork>(
            sp => sp.GetRequiredService<TContext>());
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        return services;
    }

}