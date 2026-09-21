namespace Modulus.UI;

/// <summary>One breadcrumb. A null <paramref name="Url"/> renders plain text (the current page is always plain).</summary>
/// <param name="Title">Display text.</param>
/// <param name="Url">Link target, or null.</param>
public sealed record BreadcrumbItem(string Title, string? Url = null);

/// <summary>
/// Contributes breadcrumbs for a page id. Registered via <c>AddBreadcrumbContributor&lt;T&gt;()</c>
/// (scoped). Contributors run in registration order and may replace the trail
/// (<see cref="BreadcrumbContext.Items"/> is mutable), so an app can rewrite a module's trail.
/// </summary>
public interface IBreadcrumbContributor
{
    /// <summary>Adds or reshapes <see cref="BreadcrumbContext.Items"/> for <see cref="BreadcrumbContext.PageId"/>.</summary>
    ValueTask ConfigureAsync(BreadcrumbContext context, CancellationToken cancellationToken = default);
}

/// <summary>Context passed to <see cref="IBreadcrumbContributor"/> implementations.</summary>
/// <param name="pageId">Logical page identifier the trail is built for.</param>
public sealed class BreadcrumbContext(string pageId)
{
    /// <summary>Logical page identifier the trail is built for.</summary>
    public string PageId { get; } = pageId;

    /// <summary>The trail, root first; the last item is the current page.</summary>
    public IList<BreadcrumbItem> Items { get; } = [];

    /// <summary>Validates and appends an item.</summary>
    public BreadcrumbContext Add(string title, string? url = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Breadcrumb title cannot be empty.", nameof(title));

        Items.Add(new BreadcrumbItem(title, url));
        return this;
    }
}

/// <summary>Read-only view over the breadcrumb trail for a page.</summary>
public interface IBreadcrumbProvider
{
    /// <summary>The trail for <paramref name="pageId"/> (empty when no contributor supplies one).</summary>
    IReadOnlyList<BreadcrumbItem> GetItems(string pageId);
}

/// <summary>Default <see cref="IBreadcrumbProvider"/>: runs every contributor for the page in registration order.</summary>
public sealed class BreadcrumbProvider(IEnumerable<IBreadcrumbContributor> contributors) : IBreadcrumbProvider
{
    private readonly IReadOnlyList<IBreadcrumbContributor> _contributors =
        (contributors ?? throw new ArgumentNullException(nameof(contributors))).ToList();

    /// <inheritdoc />
    public IReadOnlyList<BreadcrumbItem> GetItems(string pageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);

        var context = new BreadcrumbContext(pageId);
        foreach (var contributor in _contributors)
        {
            // Synchronous contract shared with the menu/toolbar providers; see UiNavigationRegistry.GetMenu.
#pragma warning disable VSTHRD002
            contributor.ConfigureAsync(context, CancellationToken.None).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }

        return context.Items.ToList();
    }
}
