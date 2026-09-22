namespace Modulus.UI.Notifications;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Localization;

/// <summary>
/// Registers the Notifications UI: Razor Pages services, the
/// <see cref="NotificationsUiModule"/> navigation sidecar, options bound from
/// the <c>NotificationsUi</c> section, the <c>notifications:view</c>
/// permission declaration, and startup seeding of the
/// <c>Modulus.Notifications</c> localizer resource. The host must still call
/// <c>MapRazorPages()</c> (or <see cref="MapModulusNotificationsUi"/>) so the
/// RCL pages get endpoints.
/// </summary>
public static class NotificationsUiExtensions
{
    public static IServiceCollection AddModulusNotificationsUi(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = services.AddOptions<NotificationsUiOptions>();
        if (configuration is not null)
            options.Bind(configuration.GetSection(NotificationsUiOptions.SectionName));

        var pages = services.AddRazorPages();
        services.AddUiModule<NotificationsUiModule>();
        services.AddPermissions("Notifications", registry => registry.Add(
            NotificationsUiPermissions.View,
            "View and manage your own notifications."));

        // Folder convention needs the value at registration time, so read it
        // eagerly; the bound options remain the runtime source of truth.
        var requirePermission = configuration
            ?.GetSection(NotificationsUiOptions.SectionName)
            .Get<NotificationsUiOptions>()?.RequirePermission;
        if (!string.IsNullOrWhiteSpace(requirePermission))
            pages.AddRazorPagesOptions(o => o.Conventions.AuthorizeFolder("/Notifications", requirePermission));

        services.AddLocalizationSeed(NotificationsUiLocalization.SeedAsync);
        return services;
    }

    /// <summary>Maps Razor Pages endpoints (hosts the RCL notification pages).</summary>
    public static IEndpointRouteBuilder MapModulusNotificationsUi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRazorPages();
        return endpoints;
    }
}
