namespace Modulus.Navigation;

using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Administration;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;

/// <summary>
/// The menu API. <c>GET {prefix}/menu</c> serves the caller's menu to any signed-in person. The administration routes (declared items with
/// the company's overrides, and writing or removing an override) need <see cref="ManagePermission"/>.
/// </summary>
public static class NavigationEndpoints
{
    /// <summary>Changes the company's overrides to the menu.</summary>
    public const string ManagePermission = "navigation:manage";

    /// <summary>Registers the permission. Requires <c>AddModulusAuthorization</c> for it to be enforced.</summary>
    public static IServiceCollection AddModulusNavigationAdministration(this IServiceCollection services)
        => services.AddPermissions("Modulus.Navigation", registry =>
            registry.Add(ManagePermission, "Hide, retitle, move or reorder menu items for the current company.", null, PermissionSensitivity.Sensitive));

    /// <summary>Maps <c>GET {prefix}/menu</c>, <c>GET {prefix}/definitions</c> and <c>PUT|DELETE {prefix}/overrides/{key}</c> (default prefix <c>/api/navigation</c>). Returns the administration group.</summary>
    public static RouteGroupBuilder MapModulusNavigation(this IEndpointRouteBuilder endpoints, string prefix = "/api/navigation")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        // Two groups on purpose: a route group's authorization applies to every route mapped in it, so the menu must not share one with the admin routes.
        endpoints.MapGroup(prefix).MapGet("/menu", async (ClaimsPrincipal user, INavigationService navigation, CancellationToken ct) =>
            Results.Ok(await navigation.GetMenuAsync(user, ct)))
            .RequireAuthorization();

        var admin = endpoints.MapGroup(prefix).RequireAuthorization(ManagePermission);

        admin.MapGet("/definitions", async (INavigationService navigation, CancellationToken ct) =>
            Results.Ok(await navigation.ListAsync(ct)));

        admin.MapPut("/overrides/{key}", (string key, NavigationOverride change, INavigationService navigation, CancellationToken ct) =>
            AdministrationResults.AnswerAsync(() => navigation.SetOverrideAsync(key, change, ct)));

        admin.MapDelete("/overrides/{key}", (string key, INavigationService navigation, CancellationToken ct) =>
            AdministrationResults.AnswerAsync(() => navigation.RemoveOverrideAsync(key, ct)));

        return admin;
    }
}
