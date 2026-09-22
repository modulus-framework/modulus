namespace Modulus.UI.Users;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Localization;

/// <summary>
/// Registers the Users UI: Razor Pages services, the
/// <see cref="UsersUiModule"/> navigation sidecar, options bound from the
/// <c>UsersUi</c> section, the <c>users:manage</c> permission declaration,
/// and startup seeding of the <c>Modulus.Users</c> localizer resource. The
/// host must still call <c>MapRazorPages()</c> (or
/// <see cref="MapModulusUsersUi"/>) so the RCL pages get endpoints.
/// </summary>
public static class UsersUiExtensions
{
    public static IServiceCollection AddModulusUsersUi(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        var options = services.AddOptions<UsersUiOptions>();
        if (configuration is not null)
            options.Bind(configuration.GetSection(UsersUiOptions.SectionName));

        var pages = services.AddRazorPages();
        services.AddUiModule<UsersUiModule>();
        services.AddPermissions("Users", registry => registry.Add(
            UsersUiPermissions.Manage,
            "Administer users and roles."));

        // Folder conventions need the value at registration time, so read it
        // eagerly; the bound options remain the runtime source of truth.
        // IConfigurationSection.Get<T>() returns null (not a default-
        // constructed instance) when the section is absent — the common case
        // for a host that hasn't touched UsersUi:RequirePermission — so the
        // fallback to the compiled-in permission must happen here, not only
        // on the options class's property initializer.
        var requirePermission = configuration
            ?.GetSection(UsersUiOptions.SectionName)
            .Get<UsersUiOptions>()?.RequirePermission
            ?? UsersUiPermissions.Manage;
        if (!string.IsNullOrWhiteSpace(requirePermission))
        {
            pages.AddRazorPagesOptions(o =>
            {
                o.Conventions.AuthorizeFolder("/Users", requirePermission);
                o.Conventions.AuthorizeFolder("/Roles", requirePermission);
            });
        }

        services.AddLocalizationSeed(UsersUiLocalization.SeedAsync);
        return services;
    }

    /// <summary>Maps Razor Pages endpoints (hosts the RCL user/role pages).</summary>
    public static IEndpointRouteBuilder MapModulusUsersUi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapRazorPages();
        return endpoints;
    }
}
