namespace Modulus.UI;

/// <summary>
/// Pure permission filter for the modular menu. An item is visible when its
/// <c>RequiredPermission</c> is empty or granted; groups (items with children)
/// stay visible only when they are granted themselves AND lead somewhere —
/// either a real <c>Url</c> or at least one visible child. Placeholder groups
/// (<c>Url == "#"</c>) with no visible children are dropped.
/// </summary>
public static class UiMenuFilter
{
    /// <summary>Filters a menu tree with a permission predicate.</summary>
    /// <param name="menu">Unfiltered menu tree.</param>
    /// <param name="isGranted">True when the permission is held. Null/empty permission names are always visible.</param>
    public static IReadOnlyList<UiMenuItem> Apply(
        IEnumerable<UiMenuItem> menu,
        Func<string, bool> isGranted)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(isGranted);

        var result = new List<UiMenuItem>();
        foreach (var item in menu)
        {
            if (FilterItem(item, isGranted) is { } visible)
                result.Add(visible);
        }

        return result;
    }

    private static UiMenuItem? FilterItem(UiMenuItem item, Func<string, bool> isGranted)
    {
        if (!IsVisible(item.RequiredPermission, isGranted))
            return null;

        if (item.Children is not { Count: > 0 })
            return item;

        var children = new List<UiMenuItem>();
        foreach (var child in item.Children)
        {
            if (FilterItem(child, isGranted) is { } visible)
                children.Add(visible);
        }

        // A granted group with its own page stays even when every child is
        // hidden; a pure placeholder group with no visible children goes away.
        if (children.Count == 0 && string.Equals(item.Url, "#", StringComparison.Ordinal))
            return null;

        return item with { Children = children };
    }

    private static bool IsVisible(string? requiredPermission, Func<string, bool> isGranted)
        => string.IsNullOrWhiteSpace(requiredPermission) || isGranted(requiredPermission);
}
