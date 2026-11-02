namespace Modulus.Media.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class MediaStoreExtensions
{
    /// <summary>
    /// Stores media metadata in <typeparamref name="TContext"/>. Register the context itself with <c>AddDbContext</c>. It replaces the in-memory
    /// store whichever order <c>AddModulusMedia</c> and this are called in, and registers the context as <see cref="DbContext"/> so
    /// <c>MigrateModulusDatabasesAsync</c> migrates it.
    /// </summary>
    public static IServiceCollection AddModulusMediaStore<TContext>(this IServiceCollection services)
        where TContext : ModulusMediaDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<ModulusMediaDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        services.Replace(ServiceDescriptor.Scoped<IMediaStore, EfMediaStore>());
        return services;
    }
}
