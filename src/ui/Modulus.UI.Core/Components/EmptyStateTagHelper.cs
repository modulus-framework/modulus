using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Modulus.UI;

/// <summary>View model for the <c>EmptyState</c> component.</summary>
/// <param name="Title">Headline.</param>
/// <param name="Message">Explanatory line.</param>
/// <param name="Icon">Optional <see cref="UiIcons"/> name.</param>
/// <param name="Actions">Buttons/links shown under the message, or null.</param>
public sealed record EmptyStateModel(string? Title, string? Message, string? Icon, IHtmlContent? Actions);

/// <summary>
/// <c>&lt;m-empty-state title="No products yet" message="Create the first one."&gt;&lt;a class="btn"…&gt;&lt;/m-empty-state&gt;</c> —
/// the "nothing here" block for lists and dashboards; its content (if any) becomes the action area.
/// Markup lives in the overridable <c>EmptyState/Default</c> view.
/// </summary>
[HtmlTargetElement("m-empty-state")]
public sealed class EmptyStateTagHelper : ComponentTagHelper
{
    /// <summary>Headline.</summary>
    [HtmlAttributeName("title")]
    public string? Title { get; set; }

    /// <summary>Explanatory line.</summary>
    [HtmlAttributeName("message")]
    public string? Message { get; set; }

    /// <summary>Optional icon name (see <see cref="UiIcons"/>).</summary>
    [HtmlAttributeName("icon")]
    public string? Icon { get; set; }

    /// <inheritdoc />
    protected override string Component => "EmptyState";

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var child = await output.GetChildContentAsync();
        await RenderAsync(output, new EmptyStateModel(Title, Message, Icon, child.IsEmptyOrWhiteSpace ? null : child));
    }
}
