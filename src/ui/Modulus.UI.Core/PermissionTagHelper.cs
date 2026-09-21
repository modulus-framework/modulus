using Microsoft.AspNetCore.Razor.TagHelpers;
using Modulus.Core.Abstractions;

namespace Modulus.UI;

/// <summary>
/// Renders its children only when the current user holds the permission:
/// <code>&lt;m-permission name="Catalog.Products.Create"&gt;...&lt;/m-permission&gt;</code>
/// Fail-closed: missing/empty names and denied permissions suppress output.
/// </summary>
[HtmlTargetElement("m-permission")]
public sealed class PermissionTagHelper(ICurrentUser currentUser) : TagHelper
{
    /// <summary>Permission required to render the child content.</summary>
    public string? Name { get; set; }

    /// <inheritdoc />
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        output.TagName = null;
        if (string.IsNullOrWhiteSpace(Name) || !currentUser.HasPermission(Name))
            output.SuppressOutput();
        else
            // Preserve child content explicitly so the suppression decision is
            // the only behavior; default TagHelper would render it anyway.
            _ = await output.GetChildContentAsync();
    }
}
