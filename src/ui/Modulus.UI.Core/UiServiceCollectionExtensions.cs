using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.UI.Theming;

namespace Modulus.UI;

/// <summary>
/// DI wiring for the UI framework. Call <see cref="AddModulusUi(IServiceCollection)"/>
/// once (or the configuration overload to rebrand the shell), then
/// <see cref="AddUiModule{TUiModule}"/> per UI module (order is authoritative
/// for menu ordering, mirroring <c>IModule</c> registration order).
/// </summary>
public static class UiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the navigation registry singleton plus default shell branding.
    /// Also wires the HTMX foundation: <see cref="HtmxResponse"/> (scoped),
    /// <c>IHttpContextAccessor</c>, and antiforgery with the
    /// <c>RequestVerificationToken</c> header that <c>modulus-ui.js</c> sends
    /// on every htmx request. Safe to call multiple times.
    /// </summary>
    public static IServiceCollection AddModulusUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IUiNavigationRegistry, UiNavigationRegistry>();
        // Fail-closed default (denies every permission) so menus/slots resolve without Identity;
        // TryAdd lets the real ICurrentUser win whenever it is registered.
        services.TryAddScoped<ICurrentUser, NullCurrentUser>();
        // Same fail-closed shape for ICurrentTenant (IsHost false, TenantId null):
        // feature pages (e.g. AuditLogs) that inject it must resolve to "no tenant" —
        // not throw — when a host runs the UI framework without AddModulusMultiTenancy().
        // ICurrentTenant is a singleton: it's a stateless AsyncLocal accessor in the
        // real implementation, and so is this null default.
        services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();
        services.TryAddScoped<IUiMenuProvider, UiMenuProvider>();
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.TryAddScoped<HtmxResponse>();
        services.TryAddScoped<IToolbarProvider, ToolbarProvider>();
        services.TryAddScoped<ISlotRenderer, SlotRenderer>();
        services.TryAddScoped<IBreadcrumbProvider, BreadcrumbProvider>();

        // Component tag helpers (m-datatable, m-form, ...) render through the overridable view
        // chain, so the resolver must exist even for hosts that never register a theme.
        services.AddMemoryCache();
        services.TryAddSingleton<IModulusViewResolver, ModulusViewResolver>();
        services.AddOptions<ThemeOptions>();
        services.AddOptions<UiOptions>();
        services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
        services.AddOptions<ModulusUiOptions>();
        services.AddOptions<EntityUiOptions>();
        services.TryAddSingleton<IEntityUiRegistry, EntityUiRegistry>();
        return services;
    }

    /// <summary>
    /// Registers the navigation registry and binds shell branding
    /// (<see cref="ModulusUiOptions"/>) from the <c>ModulusUi</c> section.
    /// </summary>
    public static IServiceCollection AddModulusUi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddModulusUi();
        services.Configure<ModulusUiOptions>(configuration.GetSection(ModulusUiOptions.SectionName));
        return services;
    }

    /// <summary>
    /// Registers a UI module instance for the navigation registry to consume.
    /// Idempotent per module type.
    /// </summary>
    public static IServiceCollection AddUiModule<TUiModule>(this IServiceCollection services)
        where TUiModule : class, IUiModule, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddModulusUi();

        if (services.Any(d => d.ImplementationType == typeof(TUiModule)))
            return services;

        services.AddSingleton<IUiModule, TUiModule>();
        return services;
    }

    /// <summary>
    /// Registers a prebuilt <see cref="IUiModule"/> instance (e.g. an inline
    /// <see cref="CustomUiModule"/> for an app-specific backend module).
    /// Idempotent per runtime type.
    /// </summary>
    public static IServiceCollection AddUiModule(this IServiceCollection services, IUiModule module)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(module);
        services.AddModulusUi();

        if (services.Any(d => d.ImplementationType == module.GetType()
            || d.ImplementationInstance?.GetType() == module.GetType()))
            return services;

        services.AddSingleton<IUiModule>(module);
        return services;
    }
}
