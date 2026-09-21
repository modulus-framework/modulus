using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Modulus.UI;

/// <summary>View model for the <c>Pagination</c> component.</summary>
/// <param name="Label">Text before the page number (localizable by the caller).</param>
/// <param name="Page">Current page (1-based).</param>
/// <param name="PreviousUrl">Link to the previous page, or null when there is none.</param>
/// <param name="NextUrl">Link to the next page, or null when there is none.</param>
public sealed record PaginationModel(string Label, int Page, string? PreviousUrl, string? NextUrl);

/// <summary>Builds pager links from the current request so filters survive paging.</summary>
public static class PaginationUrls
{
    /// <summary>Query keys never carried onto page links: paging navigation always targets the full page.</summary>
    private static readonly string[] Dropped = ["handler"];

    /// <summary>
    /// The current request's path and query with <paramref name="pageParam"/> set to
    /// <paramref name="page"/>, the <c>handler</c> key dropped, and
    /// <paramref name="overrides"/> layered on top (a null value removes the key).
    /// </summary>
    public static string Build(
        HttpRequest request,
        string pageParam,
        int page,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageParam);

        var query = new List<KeyValuePair<string, string?>>();
        foreach (var (key, values) in request.Query)
        {
            if (key.Equals(pageParam, StringComparison.OrdinalIgnoreCase)
                || Dropped.Contains(key, StringComparer.OrdinalIgnoreCase)
                || (overrides?.ContainsKey(key) ?? false))
            {
                continue;
            }

            query.AddRange(values.Select(v => new KeyValuePair<string, string?>(key, v)));
        }

        if (overrides is not null)
        {
            query.AddRange(overrides.Where(o => o.Value is not null));
        }

        var queryString = QueryString.Create(query);
        queryString = queryString.Add(pageParam, page.ToString(CultureInfo.InvariantCulture));
        return $"{request.PathBase}{request.Path}{queryString}";
    }
}

/// <summary>
/// <c>&lt;m-pagination page="2" has-previous="true" has-next="true" /&gt;</c> — previous/next links
/// built from the current URL, so filter parameters survive paging. Extra or overriding
/// values go in as <c>route-Name="value"</c> attributes.
/// </summary>
[HtmlTargetElement("m-pagination")]
public sealed class PaginationTagHelper : ComponentTagHelper
{
    /// <summary>Current page (1-based).</summary>
    [HtmlAttributeName("page")]
    public int Page { get; set; } = 1;

    /// <summary>Show a previous-page link.</summary>
    [HtmlAttributeName("has-previous")]
    public bool HasPrevious { get; set; }

    /// <summary>Show a next-page link.</summary>
    [HtmlAttributeName("has-next")]
    public bool HasNext { get; set; }

    /// <summary>Query key carrying the page number. Default <c>PageNumber</c>.</summary>
    [HtmlAttributeName("page-param")]
    public string PageParam { get; set; } = "PageNumber";

    /// <summary>Text before the page number. Default <c>Page</c>.</summary>
    [HtmlAttributeName("label")]
    public string Label { get; set; } = "Page";

    /// <summary>Extra query values (<c>route-UnreadOnly="true"</c>); a null value removes the key.</summary>
    [HtmlAttributeName("route", DictionaryAttributePrefix = "route-")]
    public IDictionary<string, string?> RouteValues { get; set; } = new Dictionary<string, string?>();

    /// <inheritdoc />
    protected override string Component => "Pagination";

    /// <inheritdoc />
    public override Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        => RenderAsync(output, Create(ViewContext.HttpContext.Request, Label, Page, HasPrevious, HasNext, PageParam, RouteValues));

    /// <summary>Builds the model shared by <c>m-pagination</c> and <c>m-datatable</c>.</summary>
    public static PaginationModel Create(
        HttpRequest request,
        string label,
        int page,
        bool hasPrevious,
        bool hasNext,
        string pageParam,
        IDictionary<string, string?> routeValues)
    {
        var overrides = new Dictionary<string, string?>(routeValues, StringComparer.OrdinalIgnoreCase);
        return new PaginationModel(
            label,
            page,
            hasPrevious ? PaginationUrls.Build(request, pageParam, page - 1, overrides) : null,
            hasNext ? PaginationUrls.Build(request, pageParam, page + 1, overrides) : null);
    }
}
