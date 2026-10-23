namespace Modulus.Navigation;

/// <summary>
/// A company's change to one item of the menu. It can hide the item, retitle it, move it under another parent, or reorder it. It cannot
/// make an item visible that its permission hides, and it cannot change the item's link or permission: an override only narrows the
/// menu, never widens what a person may reach.
/// </summary>
/// <param name="Hidden">Hide the item (and everything under it) for this company. <see langword="null"/> leaves it as declared.</param>
/// <param name="Title">A different title. <see langword="null"/> keeps the declared one.</param>
/// <param name="Order">A different position among its siblings. <see langword="null"/> keeps the declared one.</param>
/// <param name="ParentKey">Another parent (an item the menu declares, not one under this item). <see langword="null"/> keeps the declared one.</param>
public sealed record NavigationOverride(bool? Hidden = null, string? Title = null, int? Order = null, string? ParentKey = null);

/// <summary>The effective item after overrides, as a person in the company sees its position.</summary>
/// <param name="Item">The declared item.</param>
/// <param name="Override">The company's override, or <see langword="null"/> when there is none.</param>
public sealed record NavigationDefinitionView(NavigationItem Item, NavigationOverride? Override);

/// <summary>One item of a person's menu.</summary>
/// <param name="Key">The item.</param>
/// <param name="Title">The title in effect.</param>
/// <param name="Url">The link, or <see langword="null"/> for a group.</param>
/// <param name="Icon">The icon, if the item has one.</param>
/// <param name="Children">The items visible under this one.</param>
public sealed record NavigationNode(string Key, string Title, string? Url, string? Icon, IReadOnlyList<NavigationNode> Children);
