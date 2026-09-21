using FluentAssertions;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Modulus.UI;
using Xunit;

namespace Modulus.UI.Core.Tests;

/// <summary>
/// Spec for <c>&lt;m-page-header&gt;</c>: title/subtitle/actions rendering.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PageHeaderTagHelperTests
{
    private static (TagHelperContext Ctx, TagHelperOutput Out) Io(string? childHtml)
    {
        var ctx = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            Guid.NewGuid().ToString());
        var output = new TagHelperOutput(
            "m-page-header",
            new TagHelperAttributeList(),
            (_, _) =>
            {
                var content = new DefaultTagHelperContent();
                if (childHtml is not null)
                    content.SetHtmlContent(childHtml);
                return Task.FromResult<TagHelperContent>(content);
            });
        return (ctx, output);
    }

    private static string Html(TagHelperOutput output) => output.Content.GetContent();

    [Fact]
    public async Task RendersTitle_WithoutActionsColumn_WhenNoChildContent()
    {
        var (ctx, output) = Io(null);
        var helper = new PageHeaderTagHelper { Title = "Users" };

        await helper.ProcessAsync(ctx, output);

        output.TagName.Should().Be("div");
        output.Attributes["class"]?.Value.Should().Be("page-header d-print-none");
        var html = Html(output);
        html.Should().Contain("<h2 class=\"page-title\">Users</h2>");
        html.Should().NotContain("col-auto ms-auto");
        html.Should().NotContain("text-secondary mt-1");
    }

    [Fact]
    public async Task SelfClosingUsage_StillRendersTitleAndSubtitle()
    {
        // <m-page-header title="Audit" subtitle="All events" /> arrives in self-closing mode,
        // where content set by the helper is discarded unless the mode is switched.
        var (ctx, output) = Io(null);
        output.TagMode = TagMode.SelfClosing;
        var helper = new PageHeaderTagHelper { Title = "Audit", Subtitle = "All events" };

        await helper.ProcessAsync(ctx, output);

        output.TagMode.Should().Be(TagMode.StartTagAndEndTag);
        Html(output).Should().Contain("<h2 class=\"page-title\">Audit</h2>").And.Contain("All events");
    }

    [Fact]
    public async Task RendersSubtitle_WhenProvided()
    {
        var (ctx, output) = Io(null);
        var helper = new PageHeaderTagHelper { Title = "Tenants", Subtitle = "Current: acme" };

        await helper.ProcessAsync(ctx, output);

        Html(output).Should().Contain("<div class=\"text-secondary mt-1\">Current: acme</div>");
    }

    [Fact]
    public async Task RendersActionsSlot_WhenChildContentPresent()
    {
        var (ctx, output) = Io("<a class=\"btn\">New</a>");
        var helper = new PageHeaderTagHelper { Title = "Users" };

        await helper.ProcessAsync(ctx, output);

        var html = Html(output);
        html.Should().Contain("<div class=\"col-auto ms-auto\">");
        html.Should().Contain("<a class=\"btn\">New</a>");
    }

    [Fact]
    public async Task EncodesTitleAndSubtitle()
    {
        var (ctx, output) = Io(null);
        var helper = new PageHeaderTagHelper { Title = "<script>", Subtitle = "<b>sub</b>" };

        await helper.ProcessAsync(ctx, output);

        var html = Html(output);
        html.Should().Contain("&lt;script&gt;");
        html.Should().Contain("&lt;b&gt;sub&lt;/b&gt;");
        html.Should().NotContain("<script>");
    }

    [Fact]
    public async Task WhitespaceChildContent_OmitsActionsColumn()
    {
        var (ctx, output) = Io("   ");
        var helper = new PageHeaderTagHelper { Title = "Users" };

        await helper.ProcessAsync(ctx, output);

        Html(output).Should().NotContain("col-auto ms-auto");
    }
}
