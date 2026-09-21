using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Modulus.UI;

/// <summary>
/// Per-page metadata pages declare once so toolbars, breadcrumbs, slots and tests can target
/// them: <c>@{ ViewData.SetPageId("Catalog.Products.Index"); }</c>.
/// </summary>
public static class UiViewDataExtensions
{
    private const string PageIdKey = "Modulus.PageId";
    private const string BreadcrumbsKey = "Modulus.Breadcrumbs";

    /// <summary>Declares the page's logical id (<c>{Module}.{Area}.{Action}</c>).</summary>
    public static void SetPageId(this ViewDataDictionary viewData, string pageId)
    {
        ArgumentNullException.ThrowIfNull(viewData);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        viewData[PageIdKey] = pageId;
    }

    /// <summary>The page id set by <see cref="SetPageId"/>, or null.</summary>
    public static string? GetPageId(this ViewDataDictionary viewData)
    {
        ArgumentNullException.ThrowIfNull(viewData);
        return viewData[PageIdKey] as string;
    }

    /// <summary>Sets an explicit breadcrumb trail (the last item is the current page).</summary>
    public static void SetBreadcrumbs(this ViewDataDictionary viewData, params BreadcrumbItem[] items)
    {
        ArgumentNullException.ThrowIfNull(viewData);
        ArgumentNullException.ThrowIfNull(items);
        viewData[BreadcrumbsKey] = items.ToList();
    }

    /// <summary>The explicit trail set by <see cref="SetBreadcrumbs"/>, or null.</summary>
    public static IReadOnlyList<BreadcrumbItem>? GetBreadcrumbs(this ViewDataDictionary viewData)
    {
        ArgumentNullException.ThrowIfNull(viewData);
        return viewData[BreadcrumbsKey] as IReadOnlyList<BreadcrumbItem>;
    }
}
