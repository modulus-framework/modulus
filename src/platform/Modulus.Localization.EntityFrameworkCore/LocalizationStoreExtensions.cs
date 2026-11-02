namespace Modulus.Localization.EntityFrameworkCore;

using Microsoft.Extensions.DependencyInjection;

public static class LocalizationStoreExtensions
{
    /// <summary>
    /// Replaces the in-memory localization store with the database-backed one. Register the application's context with
    /// <c>AddDbContextFactory&lt;TContext&gt;</c> first. Call after <c>AddModulusLocalization</c>, or before it (the in-memory
    /// default is registered with <c>TryAdd</c>).
    /// </summary>
    public static IServiceCollection AddModulusEfLocalizationStore<TContext>(this IServiceCollection services)
        where TContext : ModulusLocalizationDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<Modulus.Localization.ILocalizationStore, EfLocalizationStore<TContext>>();
        return services;
    }
}
