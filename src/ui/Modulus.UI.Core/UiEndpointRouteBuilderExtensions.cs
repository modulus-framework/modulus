using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Modulus.UI;

/// <summary>
/// Maps the UI framework endpoints: the modular sidebar menu (consumed by the
/// Tabler shell and SPA clients) and the registered UI module manifests.
/// </summary>
public static class UiEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps <c>GET {prefix}/menu</c> (navigation tree, filtered to the current
    /// user via <see cref="IUiMenuProvider"/>) and
    /// <c>GET {prefix}/modules</c> (manifests). Entries keep their
    /// <c>requiredPermission</c> metadata so SPA clients can reason about them;
    /// unauthorized entries are already removed server-side.
    /// </summary>
    public static RouteGroupBuilder MapModulusUiMenu(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/_ui")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix);

        group.MapGet("/menu", (IUiMenuProvider menu) =>
            Results.Ok(menu.GetMenu()));

        group.MapGet("/modules", (IUiNavigationRegistry registry) =>
            Results.Ok(registry.GetModules()));

        return group;
    }
}
