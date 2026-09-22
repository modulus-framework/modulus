namespace Modulus.UI.AuditLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Localization;

/// <summary>
/// Registers the Audit Logging UI: Razor Pages services, the
/// <see cref="AuditLoggingUiModule"/> navigation sidecar, options bound from
/// the <c>AuditLoggingUi</c> section, the <c>audit:view</c> permission
/// declaration, and startup seeding of the <c>Modulus.AuditLogging</c>
/// localizer resource. The host must still call <c>MapRazorPages()</c> (or
/// <see cref="MapModulusAuditLoggingUi"/>) so the RCL pages get endpoints.
/// </summary>
public static class AuditLoggingUiExtensions
{
    public static IServiceCollection AddModulusAuditLoggingUi(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = services.AddOptions<AuditLoggingUiOptions>();
        if (configuration is not null)
            options.Bind(configuration.GetSection(AuditLoggingUiOptions.SectionName));

        var pages = services.AddRazorPages();
        services.AddUiModule<AuditLoggingUiModule>();
        services.AddPermissions("AuditLogging", registry => registry.Add(
            AuditLoggingUiPermissions.View,
            "View audit-log entries."));

        // Folder convention needs the value at registration time, so read it
        // eagerly; the bound options remain the runtime source of truth.
        // IConfigurationSection.Get<T>() returns null (not a default-
        // constructed instance) when the section is absent — the common case
        // for a host that hasn't touched AuditLoggingUi:RequirePermission —
        // so the fallback to the compiled-in permission must happen here,
        // not only on the options class's property initializer.
        var requirePermission = configuration
            ?.GetSection(AuditLoggingUiOptions.SectionName)
            .Get<AuditLoggingUiOptions>()?.RequirePermission
            ?? AuditLoggingUiPermissions.View;
        if (!string.IsNullOrWhiteSpace(requirePermission))
            pages.AddRazorPagesOptions(o => o.Conventions.AuthorizeFolder("/AuditLogs", requirePermission));

        services.AddLocalizationSeed(AuditLoggingUiLocalization.SeedAsync);
        return services;
    }

    /// <summary>Maps Razor Pages endpoints (hosts the RCL audit-log pages).</summary>
    public static IEndpointRouteBuilder MapModulusAuditLoggingUi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRazorPages();
        return endpoints;
    }
}
