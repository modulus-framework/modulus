using FluentAssertions;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>
/// The CLI-generated UI shell resolves its layout with <c>Context.GetThemeLayout()</c>; with the
/// Tabler theme registered (<c>modulus ui add Tabler</c>) that must land in the Application layout.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CliGeneratedShellTests
{
    [Fact]
    public async Task GetThemeLayout_from_the_generated_shell_renders_the_tabler_application_layout()
    {
        await using var host = await ThemeHost.StartAsync();

        var html = await host.GetStringAsync("/probe/GeneratedShell");

        html.Should().Contain("m-layout-application").And.Contain("id=\"generated-body\"");
    }

    [Fact]
    public async Task A_generated_style_razor_page_gets_the_theme_layout_but_not_for_htmx_fragments()
    {
        await using var host = await ThemeHost.StartAsync();

        var full = await host.GetStringAsync("/Probe/GeneratedPage");
        full.Should().Contain("m-layout-application").And.Contain("id=\"generated-page\"");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/Probe/GeneratedPage");
        request.Headers.Add("HX-Request", "true");
        using var response = await host.Client.SendAsync(request);
        var fragment = await response.Content.ReadAsStringAsync();

        fragment.Trim().Should().Be("<div id=\"generated-page\">content</div>", "a fragment swaps in without the shell");
    }
}
