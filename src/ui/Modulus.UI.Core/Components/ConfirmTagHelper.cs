using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Modulus.UI;

/// <summary>View model for the <c>Confirm</c> component.</summary>
/// <param name="Message">Question shown in the confirmation dialog.</param>
/// <param name="Post">URL posted with htmx after confirmation (or null).</param>
/// <param name="Delete">URL sent as an htmx DELETE after confirmation (or null).</param>
/// <param name="Target">htmx target selector (null: the button itself).</param>
/// <param name="Swap">htmx swap strategy (null: the htmx default).</param>
/// <param name="Class">Button classes.</param>
/// <param name="Body">Button content.</param>
public sealed record ConfirmModel(
    string Message,
    string? Post,
    string? Delete,
    string? Target,
    string? Swap,
    string Class,
    IHtmlContent Body);

/// <summary>
/// <c>&lt;m-confirm message="Delete this user?" delete="/users/4" target="closest tr" swap="outerHTML"&gt;Delete&lt;/m-confirm&gt;</c> —
/// a button that asks before it sends the htmx request. The question is shown by the theme's confirm
/// dialog (<c>hx-confirm</c> is routed to <c>Modulus.confirm</c>), so no inline script is needed.
/// Markup lives in the overridable <c>Confirm/Default</c> view.
/// </summary>
[HtmlTargetElement("m-confirm")]
public sealed class ConfirmTagHelper : ComponentTagHelper
{
    /// <summary>Question shown in the dialog (required).</summary>
    [HtmlAttributeName("message")]
    public string? Message { get; set; }

    /// <summary>URL to POST after confirmation.</summary>
    [HtmlAttributeName("post")]
    public string? Post { get; set; }

    /// <summary>URL to DELETE after confirmation.</summary>
    [HtmlAttributeName("delete")]
    public string? Delete { get; set; }

    /// <summary>htmx target selector.</summary>
    [HtmlAttributeName("target")]
    public string? Target { get; set; }

    /// <summary>htmx swap strategy.</summary>
    [HtmlAttributeName("swap")]
    public string? Swap { get; set; }

    /// <summary>Button classes. Default <c>btn btn-danger</c>.</summary>
    [HtmlAttributeName("class")]
    public string Class { get; set; } = "btn btn-danger";

    /// <inheritdoc />
    protected override string Component => "Confirm";

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrWhiteSpace(Message))
        {
            throw new InvalidOperationException("<m-confirm> requires a message.");
        }

        if (string.IsNullOrWhiteSpace(Post) == string.IsNullOrWhiteSpace(Delete))
        {
            throw new InvalidOperationException("<m-confirm> requires exactly one of post or delete.");
        }

        var body = await output.GetChildContentAsync();
        await RenderAsync(output, new ConfirmModel(Message, Post, Delete, Target, Swap, Class, body));
    }
}
