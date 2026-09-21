namespace Modulus.UI;

/// <summary>
/// A single sidebar menu entry contributed by a UI module. Entries with the same
/// <see cref="Id"/> are deduplicated (first registration wins). Children render
/// as a submenu group.
/// </summary>
/// <param name="Id">Stable dotted id (e.g. <c>Identity.Users</c>).</param>
/// <param name="Title">Display title.</param>
/// <param name="Url">Navigation target.</param>
/// <param name="Icon">Optional Tabler icon name.</param>
/// <param name="RequiredPermission">
/// Permission required to see/use the entry. Exposed to SPA clients so they can
/// hide entries; server-side filtering lands once Authorization is wired.
/// </param>
/// <param name="Order">Sort key within its level (lower first, then title).</param>
/// <param name="Children">Submenu entries.</param>
public sealed record UiMenuItem(
    string Id,
    string Title,
    string Url,
    string? Icon = null,
    string? RequiredPermission = null,
    int Order = 100,
    IReadOnlyList<UiMenuItem>? Children = null);
