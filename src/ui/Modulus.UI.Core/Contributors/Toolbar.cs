using Modulus.Core.Abstractions;

namespace Modulus.UI;

/// <summary>
/// A page-level toolbar action (buttons rendered above the page body).
/// Exactly one of <see cref="ToolbarItem.Url"/>, <see cref="ToolbarItem.HxGet"/>
/// or <see cref="ToolbarItem.HxPost"/> must be set.
/// </summary>
/// <param name="Id">Stable identifier (dedup/move key).</param>
/// <param name="Title">Display text.</param>
/// <param name="Url">Navigation target (renders an <c>&lt;a&gt;</c>).</param>
/// <param name="HxGet">htmx GET url (renders a button with <c>hx-get</c>).</param>
/// <param name="HxPost">htmx POST url (renders a button with <c>hx-post</c>).</param>
/// <param name="Icon">Icon name (see <c>_UiIcon</c>).</param>
/// <param name="RequiredPermission">Hidden unless the current user has it.</param>
/// <param name="Order">Sort key (ascending, ties by title).</param>
/// <param name="CssClass">Button color classes (defaults to <c>btn-primary</c> for the first item, <c>btn-outline-secondary</c> after).</param>
/// <param name="Target">
/// CSS selector receiving the <c>HxGet</c>/<c>HxPost</c> response (defaults to the shared modal
/// container, <c>#m-modal-container</c>).
/// </param>
public sealed record ToolbarItem(
    string Id,
    string Title,
    string? Url = null,
    string? HxGet = null,
    string? HxPost = null,
    string? Icon = null,
    string? RequiredPermission = null,
    int Order = 100,
    string? CssClass = null,
    string? Target = null);

/// <summary>Contributes toolbar items for a page. Registered via <c>AddToolbarContributor&lt;T&gt;()</c> (scoped).</summary>
public interface IToolbarContributor
{
    /// <summary>Adds items to <see cref="ToolbarContext.Items"/>.</summary>
    ValueTask ConfigureAsync(ToolbarContext context, CancellationToken cancellationToken = default);
}

/// <summary>Context passed to <see cref="IToolbarContributor"/> implementations.</summary>
/// <param name="pageId">Logical page identifier the toolbar is rendered for.</param>
public sealed class ToolbarContext(string pageId)
{
    /// <summary>Logical page identifier the toolbar is rendered for.</summary>
    public string PageId { get; } = pageId;

    /// <summary>Items contributed for this page.</summary>
    public IList<ToolbarItem> Items { get; } = [];

    /// <summary>Validates and adds an item.</summary>
    public ToolbarContext Add(ToolbarItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.Id))
            throw new ArgumentException("Toolbar item id cannot be empty.", nameof(item));
        if (string.IsNullOrWhiteSpace(item.Title))
            throw new ArgumentException("Toolbar item title cannot be empty.", nameof(item));
        if (string.IsNullOrWhiteSpace(item.Url)
            && string.IsNullOrWhiteSpace(item.HxGet)
            && string.IsNullOrWhiteSpace(item.HxPost))
        {
            throw new ArgumentException("Toolbar item must define Url, HxGet or HxPost.", nameof(item));
        }

        Items.Add(item);
        return this;
    }
}

/// <summary>Read-only, permission-filtered view over a page's toolbar.</summary>
public interface IToolbarProvider
{
    /// <summary>Items for <paramref name="pageId"/>, filtered by permission and sorted.</summary>
    IReadOnlyList<ToolbarItem> GetItems(string pageId);
}

/// <summary>
/// Default <see cref="IToolbarProvider"/>: runs every registered
/// <see cref="IToolbarContributor"/> for the page, drops entries the current
/// user may not use, sorts by <c>Order</c> then title.
/// </summary>
public sealed class ToolbarProvider(
    IEnumerable<IToolbarContributor> contributors,
    ICurrentUser currentUser) : IToolbarProvider
{
    private readonly IReadOnlyList<IToolbarContributor> _contributors =
        (contributors ?? throw new ArgumentNullException(nameof(contributors))).ToList();

    private readonly ICurrentUser _currentUser =
        currentUser ?? throw new ArgumentNullException(nameof(currentUser));

    /// <inheritdoc />
    public IReadOnlyList<ToolbarItem> GetItems(string pageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);

        var context = new ToolbarContext(pageId);
        foreach (var contributor in _contributors)
        {
            // IToolbarProvider.GetItems is a synchronous contract; see UiNavigationRegistry.GetMenu
            // for why blocking on a contributor is safe (no SynchronizationContext, stateless).
#pragma warning disable VSTHRD002
            contributor.ConfigureAsync(context, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
#pragma warning restore VSTHRD002
        }

        return context.Items
            .Where(i => i.RequiredPermission is null || _currentUser.HasPermission(i.RequiredPermission))
            .OrderBy(i => i.Order)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
