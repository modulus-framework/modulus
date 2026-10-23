namespace Modulus.Navigation;

/// <summary>
/// One entry of the application menu. Modules declare items through <see cref="INavigationContributor"/>; the menu shows an item to a
/// person only when the item has no <see cref="Permission"/> or the person holds it, and the item is not hidden by an override.
/// </summary>
/// <param name="Key">The item's identity: letters, digits, dots, hyphens and underscores, at most 128 characters, unique in the menu.</param>
/// <param name="Title">The text shown.</param>
public sealed record NavigationItem(string Key, string Title)
{
    /// <summary>The parent item's key, or <see langword="null"/> for a top-level item.</summary>
    public string? ParentKey { get; init; }

    /// <summary>
    /// Where the item leads: a path within the application that starts with <c>/</c> and not with <c>//</c>. An item with no URL is a
    /// group, shown only while one of its children is.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>The permission a person needs to see the item. <see langword="null"/> means anyone who can reach the menu.</summary>
    public string? Permission { get; init; }

    /// <summary>An icon name, interpreted by the renderer.</summary>
    public string? Icon { get; init; }

    /// <summary>Position among its siblings; lower first, then by title.</summary>
    public int Order { get; init; }
}

/// <summary>Declares navigation items. Every registered contributor runs once, at the first use of the menu, in registration order.</summary>
public interface INavigationContributor
{
    void Contribute(NavigationBuilder navigation);
}

/// <summary>Collects the items of every contributor.</summary>
public sealed class NavigationBuilder
{
    private readonly List<NavigationItem> _items = [];

    /// <summary>The items added so far, in order.</summary>
    public IReadOnlyList<NavigationItem> Items => _items;

    /// <summary>Adds an item. Keys are checked for uniqueness when the menu is built.</summary>
    public NavigationBuilder Add(NavigationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
        return this;
    }
}
