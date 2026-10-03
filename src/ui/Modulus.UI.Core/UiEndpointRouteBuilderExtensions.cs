using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Core.Abstractions.Security;

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
    /// <c>GET {prefix}/modules</c> (manifests, requires authentication — see
    /// below). Entries keep their <c>requiredPermission</c> metadata so SPA
    /// clients can reason about them; unauthorized entries are already
    /// removed server-side.
    /// </summary>
    public static RouteGroupBuilder MapModulusUiMenu(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/_ui")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix);

        // Anonymous by design and declared as such, so the startup security guard accepts it on a host without a
        // fallback policy: the tree is filtered per caller and an anonymous caller gets an empty one.
        group.MapGet("/menu", (IUiMenuProvider menu) =>
            Results.Ok(menu.GetMenu()))
            .AllowAnonymous()
            .WithMetadata(new LoosenedAttribute("Navigation tree filtered per caller; anonymous callers get an empty tree") { Framework = true });

        // Unlike /menu (already filtered per-caller by IUiMenuProvider, so an
        // anonymous caller just sees an empty tree via the fail-closed
        // NullCurrentUser default), /modules returns the full, unfiltered
        // manifest list -- installed module names and versions -- with no
        // per-entry filtering to fall back on. Require authentication so
        // that install topology isn't exposed to anonymous callers.
        group.MapGet("/modules", (IUiNavigationRegistry registry) =>
            Results.Ok(registry.GetModules()))
            .RequireAuthorization();

        return group;
    }
}
