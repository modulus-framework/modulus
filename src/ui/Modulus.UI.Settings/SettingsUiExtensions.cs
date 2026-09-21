namespace Modulus.UI.Settings;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Localization;

/// <summary>
/// Registers the Settings UI: Razor Pages services, the
/// <see cref="SettingsUiModule"/> navigation sidecar, options bound from the
/// <c>SettingsUi</c> section, the <c>settings:manage</c> permission
/// declaration, and startup seeding of the <c>Modulus.Settings</c> localizer
/// resource. The host must still call <c>MapRazorPages()</c> (or
/// <see cref="MapModulusSettingsUi"/>) so the RCL pages get endpoints.
/// </summary>
public static class SettingsUiExtensions
{
    public static IServiceCollection AddModulusSettingsUi(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = services.AddOptions<SettingsUiOptions>();
        if (configuration is not null)
            options.Bind(configuration.GetSection(SettingsUiOptions.SectionName));

        var pages = services.AddRazorPages();
        services.AddUiModule<SettingsUiModule>();
        services.AddPermissions("Settings", registry => registry.Add(
            SettingsUiPermissions.Manage,
            "View and edit settings."));

        // Folder convention needs the value at registration time, so read it
        // eagerly; the bound options remain the runtime source of truth.
        // IConfigurationSection.Get<T>() returns null (not a default-
        // constructed instance) when the section is absent — the common case
        // for a host that hasn't touched SettingsUi:RequirePermission — so the
        // fallback to the compiled-in permission must happen here, not only
        // on the options class's property initializer.
        var requirePermission = configuration
            ?.GetSection(SettingsUiOptions.SectionName)
            .Get<SettingsUiOptions>()?.RequirePermission
            ?? SettingsUiPermissions.Manage;
        if (!string.IsNullOrWhiteSpace(requirePermission))
            pages.AddRazorPagesOptions(o => o.Conventions.AuthorizeFolder("/Settings", requirePermission));

        services.AddSingleton<IStartupFilter, SettingsUiStartupFilter>();
        return services;
    }

    /// <summary>Maps Razor Pages endpoints (hosts the RCL settings pages).</summary>
    public static IEndpointRouteBuilder MapModulusSettingsUi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRazorPages();
        return endpoints;
    }

    private sealed class SettingsUiStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            var store = app.ApplicationServices.GetService(typeof(ILocalizationStore)) as ILocalizationStore;
            if (store is not null)
                SettingsUiLocalization.Seed(store);

            next(app);
        };
    }
}
