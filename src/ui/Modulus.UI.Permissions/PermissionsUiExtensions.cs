namespace Modulus.UI.Permissions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Localization;

/// <summary>
/// Registers the Permissions UI: Razor Pages services, the
/// <see cref="PermissionsUiModule"/> navigation sidecar, options bound from
/// the <c>PermissionsUi</c> section, the <c>permissions:view</c> permission
/// declaration, and startup seeding of the <c>Modulus.Permissions</c>
/// localizer resource. The host must still call <c>MapRazorPages()</c> (or
/// <see cref="MapModulusPermissionsUi"/>) so the RCL pages get endpoints.
/// </summary>
public static class PermissionsUiExtensions
{
    public static IServiceCollection AddModulusPermissionsUi(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = services.AddOptions<PermissionsUiOptions>();
        if (configuration is not null)
            options.Bind(configuration.GetSection(PermissionsUiOptions.SectionName));

        var pages = services.AddRazorPages();
        services.AddUiModule<PermissionsUiModule>();
        services.AddPermissions("Permissions", registry => registry.Add(
            PermissionsUiPermissions.View,
            "View the permission catalog and holder-grant pages."));

        // Folder convention needs the value at registration time, so read it
        // eagerly; the bound options remain the runtime source of truth.
        // IConfigurationSection.Get<T>() returns null (not a default-
        // constructed instance) when the section is absent — the common case
        // for a host that hasn't touched PermissionsUi:RequirePermission — so
        // the fallback to the compiled-in permission must happen here, not
        // only on the options class's property initializer.
        var requirePermission = configuration
            ?.GetSection(PermissionsUiOptions.SectionName)
            .Get<PermissionsUiOptions>()?.RequirePermission
            ?? PermissionsUiPermissions.View;
        if (!string.IsNullOrWhiteSpace(requirePermission))
            pages.AddRazorPagesOptions(o => o.Conventions.AuthorizeFolder("/Permissions", requirePermission));

        services.AddLocalizationSeed(PermissionsUiLocalization.SeedAsync);
        return services;
    }

    /// <summary>Maps Razor Pages endpoints (hosts the RCL permissions pages).</summary>
    public static IEndpointRouteBuilder MapModulusPermissionsUi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRazorPages();
        return endpoints;
    }
}
