namespace Modulus.UI;

/// <summary>
/// Collects menu entries from UI modules. Deduplicates by <c>Id</c> (first wins)
/// and returns entries ordered by <c>Order</c> then title.
/// </summary>
public sealed class UiNavigationBuilder
{
    private readonly Dictionary<string, UiMenuItem> _items = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds a top-level group entry (a menu item with children).</summary>
    public UiNavigationBuilder AddGroup(
        string id,
        string title,
        string? icon = null,
        string? requiredPermission = null,
        int order = 100)
    {
        Validate(id, title);
        Add(new UiMenuItem(id, title, "#", icon, requiredPermission, order, []));
        return this;
    }

    /// <summary>
    /// Adds a menu item. When <paramref name="groupId"/> is supplied, the item is
    /// attached as a child of that group (the group is auto-created when missing).
    /// </summary>
    public UiNavigationBuilder AddItem(
        string id,
        string title,
        string url,
        string? groupId = null,
        string? icon = null,
        string? requiredPermission = null,
        int order = 100)
    {
        Validate(id, title);
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Menu item URL cannot be empty.", nameof(url));

        var item = new UiMenuItem(id, title, url, icon, requiredPermission, order);

        if (string.IsNullOrWhiteSpace(groupId))
        {
            Add(item);
            return this;
        }

        if (!_items.TryGetValue(groupId, out var group))
        {
            group = new UiMenuItem(groupId, groupId, "#", null, null, order, []);
            _items[groupId] = group;
        }

        var children = group.Children!.ToList();
        if (children.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)))
            return this;

        children.Add(item);
        _items[groupId] = group with { Children = Sort(children) };
        return this;
    }

    /// <summary>
    /// Finds an entry by id, at the top level or inside any group. Null when absent.
    /// </summary>
    public UiMenuItem? Find(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_items.TryGetValue(id, out var item))
            return item;

        return _items.Values
            .SelectMany(g => g.Children ?? [])
            .FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Removes a top-level entry (and its children). False when absent.</summary>
    public bool Remove(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _items.Remove(id);
    }

    /// <summary>
    /// Re-parents an entry (top-level or a member of another group) into
    /// <paramref name="groupId"/>, auto-creating the group when missing.
    /// Optionally restamps <c>Order</c>. Contributors use this to relocate
    /// module entries without duplicating them.
    /// </summary>
    public UiNavigationBuilder MoveTo(string id, string groupId, int? order = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (string.IsNullOrWhiteSpace(groupId))
            throw new ArgumentException("Group id cannot be empty.", nameof(groupId));
        // Find covers top-level entries and group children, as the contract promises.
        var item = Find(id);
        if (item is null)
            return this;

        // Detach from any group's child list first.
        foreach (var (key, current) in _items.ToArray())
        {
            var children = (current.Children ?? []).ToList();
            var index = children.FindIndex(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                continue;
            children.RemoveAt(index);
            _items[key] = current with { Children = children };
        }

        if (string.Equals(id, groupId, StringComparison.OrdinalIgnoreCase))
            return this;

        if (!_items.TryGetValue(groupId, out var group))
        {
            group = new UiMenuItem(groupId, groupId, "#", null, null, 100, []);
            _items[groupId] = group;
        }

        // A top-level entry with this id becomes the re-parented child.
        _items.Remove(id);

        var childrenOfTarget = (group.Children ?? []).ToList();
        if (childrenOfTarget.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)))
            return this;

        var moved = order is null ? item : item with { Order = order.Value };
        childrenOfTarget.Add(moved);
        _items[groupId] = group with { Children = Sort(childrenOfTarget) };
        return this;
    }

    /// <summary>Restamps <c>Order</c> on an entry, at the top level or inside a group.</summary>
    public UiNavigationBuilder SetOrder(string id, int order)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_items.TryGetValue(id, out var item))
        {
            _items[id] = item with { Order = order };
            return this;
        }

        foreach (var (key, current) in _items.ToArray())
        {
            var children = (current.Children ?? []).ToList();
            var index = children.FindIndex(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                continue;
            children[index] = children[index] with { Order = order };
            _items[key] = current with { Children = Sort(children) };
            break;
        }

        return this;
    }

    /// <summary>Returns the top-level menu ordered by <c>Order</c> then title.</summary>
    public IReadOnlyList<UiMenuItem> Build()
        => Sort(_items.Values);

    internal IReadOnlyDictionary<string, UiMenuItem> Snapshot()
        => _items;

    internal void Merge(IReadOnlyDictionary<string, UiMenuItem> other)
    {
        foreach (var (id, item) in other)
        {
            if (_items.TryGetValue(id, out var existing))
            {
                var merged = (existing.Children ?? []).ToList();
                foreach (var child in item.Children ?? [])
                {
                    if (!merged.Any(c => string.Equals(c.Id, child.Id, StringComparison.OrdinalIgnoreCase)))
                        merged.Add(child);
                }

                _items[id] = existing with { Children = Sort(merged) };
            }
            else
            {
                _items[id] = item;
            }
        }
    }

    private void Add(UiMenuItem item)
    {
        if (!_items.ContainsKey(item.Id))
            _items[item.Id] = item;
    }

    private static void Validate(string id, string title)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Menu item id cannot be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Menu item title cannot be empty.", nameof(title));
    }

    private static IReadOnlyList<UiMenuItem> Sort(IEnumerable<UiMenuItem> items)
        => items.OrderBy(i => i.Order).ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase).ToList();
}
