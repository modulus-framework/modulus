namespace Modulus.Navigation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Modulus.Authorization.Extensions;
using Modulus.Settings;

public static class NavigationExtensions
{
    /// <summary>
    /// Registers the navigation engine: the contributors' menu (built once, checked when the host starts), the per-person menu and the
    /// company overrides (stored as settings, so <c>AddModulusSettings</c> is added too). Items a host declares inline can be passed here;
    /// modules declare theirs with <see cref="AddNavigationContributor{T}"/>. Map the API with <c>MapModulusNavigation</c>.
    /// </summary>
    public static IServiceCollection AddModulusNavigation(this IServiceCollection services, Action<NavigationBuilder>? configure = null)
    {
        services.AddModulusSettings();
        services.AddModulusNavigationAdministration();
        services.TryAddSingleton<NavigationRegistry>();
        services.TryAddScoped<INavigationService, NavigationService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, NavigationStartupCheck>());
        if (configure is not null)
            services.AddSingleton<INavigationContributor>(new DelegateNavigationContributor(configure));
        return services;
    }

    /// <summary>Adds a module's contributor. Contributors run in registration order.</summary>
    public static IServiceCollection AddNavigationContributor<T>(this IServiceCollection services)
        where T : class, INavigationContributor
    {
        services.AddSingleton<INavigationContributor, T>();
        return services;
    }
}
