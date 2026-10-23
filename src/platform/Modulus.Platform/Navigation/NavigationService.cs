namespace Modulus.Navigation;

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Modulus.Authorization;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Settings;

/// <summary>The menu, for one person, and the company's overrides to it.</summary>
public interface INavigationService
{
    /// <summary>The menu the person may see in the current company: items they hold the permission for, minus what the company hides.</summary>
    Task<IReadOnlyList<NavigationNode>> GetMenuAsync(ClaimsPrincipal user, CancellationToken ct = default);

    /// <summary>Every declared item with the current company's override, hidden items included (for administrators).</summary>
    Task<IReadOnlyList<NavigationDefinitionView>> ListAsync(CancellationToken ct = default);

    /// <summary>Stores the company's override to an item. Refused when the item or its new parent is unknown, or the new parent loops.</summary>
    Task SetOverrideAsync(string key, NavigationOverride change, CancellationToken ct = default);

    /// <summary>Removes the company's override to an item, so the declared item applies again.</summary>
    Task RemoveOverrideAsync(string key, CancellationToken ct = default);
}

/// <inheritdoc cref="INavigationService" />
/// <remarks>
/// Overrides are stored as settings (<c>navigation.override.{key}</c>, one JSON value per item at company scope), so they inherit the
/// setting store's isolation and cache. Visibility is decided here, from the declared permission, and never from an override.
/// </remarks>
public sealed class NavigationService(
    NavigationRegistry registry,
    ISettingManager settings,
    IAuthorizationService authorization) : INavigationService
{
    /// <summary>The setting name that holds an item's override.</summary>
    public static string OverrideSetting(string key) => $"navigation.override.{key}";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<NavigationNode>> GetMenuAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var definition = registry.Definition;

        // The effective position of every item the company has not hidden, and whether the person may see it.
        var placed = new Dictionary<string, Placed>(StringComparer.Ordinal);
        foreach (var item in definition.Items)
        {
            var overrideValue = await ReadOverrideAsync(item.Key, ct).ConfigureAwait(false);
            if (overrideValue?.Hidden == true)
                continue;

            var permitted = item.Permission is null || (await authorization.AuthorizeAsync(user, null, new PermissionRequirement(item.Permission)).ConfigureAwait(false)).Succeeded;
            if (!permitted)
                continue;

            placed[item.Key] = new Placed(item, overrideValue?.Title ?? item.Title, overrideValue?.Order ?? item.Order, overrideValue?.ParentKey ?? item.ParentKey);
        }

        return Children(placed, parentKey: null);
    }

    public async Task<IReadOnlyList<NavigationDefinitionView>> ListAsync(CancellationToken ct = default)
    {
        var views = new List<NavigationDefinitionView>();
        foreach (var item in registry.Definition.Items)
            views.Add(new NavigationDefinitionView(item, await ReadOverrideAsync(item.Key, ct).ConfigureAwait(false)));
        return views;
    }

    public async Task SetOverrideAsync(string key, NavigationOverride change, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        RequireItem(key);

        if (change.Title is not null && (string.IsNullOrWhiteSpace(change.Title) || change.Title.Length > NavigationDefinition.MaxTitleLength))
            throw new ValidationException([$"A title is at most {NavigationDefinition.MaxTitleLength} characters and not blank."]);

        if (change.ParentKey is not null)
        {
            if (!registry.Definition.ByKey.ContainsKey(change.ParentKey))
                throw new ValidationException([$"'{change.ParentKey}' is not a menu item."]);
            await CheckParentAsync(key, change.ParentKey, ct).ConfigureAwait(false);
        }

        if (change.Hidden is null && change.Title is null && change.Order is null && change.ParentKey is null)
        {
            await RemoveOverrideAsync(key, ct).ConfigureAwait(false);
            return;
        }

        await settings.SetAsync(OverrideSetting(key), JsonSerializer.Serialize(change, JsonOptions), SettingScope.Tenant, ct).ConfigureAwait(false);
    }

    public async Task RemoveOverrideAsync(string key, CancellationToken ct = default)
    {
        RequireItem(key);
        await settings.RemoveAsync(OverrideSetting(key), SettingScope.Tenant, ct).ConfigureAwait(false);
    }

    private NavigationItem RequireItem(string key)
    {
        if (!registry.Definition.ByKey.TryGetValue(key, out var item))
            throw new NotFoundException($"No menu item named '{key}' is declared.");
        return item;
    }

    // A new parent is refused when it loops back to the item, or the item would sit deeper than the menu allows. The parents of all
    // other items come from their own overrides, read as they are now.
    private async Task CheckParentAsync(string key, string parentKey, CancellationToken ct)
    {
        var parents = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var item in registry.Definition.Items)
            parents[item.Key] = (await ReadOverrideAsync(item.Key, ct).ConfigureAwait(false))?.ParentKey ?? item.ParentKey;
        parents[key] = parentKey;

        var depth = NavigationDefinition.Depth(key, id => parents.GetValueOrDefault(id), out var cyclic);
        if (cyclic)
            throw new ValidationException([$"'{parentKey}' is below '{key}' in the menu, so it cannot be its parent."]);
        if (depth > NavigationDefinition.MaxDepth)
            throw new ValidationException([$"The menu nests at most {NavigationDefinition.MaxDepth} levels."]);
    }

    private async Task<NavigationOverride?> ReadOverrideAsync(string key, CancellationToken ct)
    {
        var raw = (await settings.ResolveAsync(OverrideSetting(key), ct).ConfigureAwait(false)).Value;
        if (raw is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<NavigationOverride>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            // A value that is not an override is ignored: the declared item applies.
            return null;
        }
    }

    private sealed record Placed(NavigationItem Item, string Title, int Order, string? ParentKey);

    // Built from the roots down, so an item is reached only through visible ancestors; an item whose parent is hidden, missing or
    // part of a loop is never reached and so never shown.
    private static IReadOnlyList<NavigationNode> Children(IReadOnlyDictionary<string, Placed> placed, string? parentKey)
    {
        var nodes = new List<(Placed Item, NavigationNode Node)>();
        foreach (var entry in placed.Values.Where(p => p.ParentKey == parentKey))
        {
            var children = Children(placed, entry.Item.Key);
            if (entry.Item.Url is null && children.Count == 0)
                continue;
            nodes.Add((entry, new NavigationNode(entry.Item.Key, entry.Title, entry.Item.Url, entry.Item.Icon, children)));
        }

        return nodes
            .OrderBy(n => n.Item.Order)
            .ThenBy(n => n.Item.Title, StringComparer.Ordinal)
            .Select(n => n.Node)
            .ToList();
    }
}
