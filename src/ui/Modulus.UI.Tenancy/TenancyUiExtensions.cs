namespace Modulus.UI.Tenancy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Localization;

/// <summary>
/// Registers the Tenancy UI: Razor Pages services, the
/// <see cref="TenancyUiModule"/> navigation sidecar, options bound from the
/// <c>TenancyUi</c> section, the <c>tenancy:view</c> permission declaration,
/// and startup seeding of the <c>Modulus.Tenancy</c> localizer resource. The
/// host must still call <c>MapRazorPages()</c> (or
/// <see cref="MapModulusTenancyUi"/>) so the RCL pages get endpoints.
/// </summary>
public static class TenancyUiExtensions
{
    public static IServiceCollection AddModulusTenancyUi(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = services.AddOptions<TenancyUiOptions>();
        if (configuration is not null)
            options.Bind(configuration.GetSection(TenancyUiOptions.SectionName));

        var pages = services.AddRazorPages();
        services.AddUiModule<TenancyUiModule>();
        services.AddPermissions("Tenancy", registry => registry.Add(
            TenancyUiPermissions.View,
            "View the tenant directory and tenant details."));

        // Folder convention needs the value at registration time, so read it
        // eagerly; the bound options remain the runtime source of truth.
        var requirePermission = configuration
            ?.GetSection(TenancyUiOptions.SectionName)
            .Get<TenancyUiOptions>()?.RequirePermission;
        if (!string.IsNullOrWhiteSpace(requirePermission))
            pages.AddRazorPagesOptions(o => o.Conventions.AuthorizeFolder("/Tenancy", requirePermission));

        services.AddSingleton<IStartupFilter, TenancyUiStartupFilter>();
        return services;
    }

    /// <summary>Maps Razor Pages endpoints (hosts the RCL tenancy pages).</summary>
    public static IEndpointRouteBuilder MapModulusTenancyUi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRazorPages();
        return endpoints;
    }

    private sealed class TenancyUiStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            var store = app.ApplicationServices.GetService(typeof(ILocalizationStore)) as ILocalizationStore;
            if (store is not null)
                TenancyUiLocalization.Seed(store);

            next(app);
        };
    }
}
