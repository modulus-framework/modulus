using System.Net;
using FluentAssertions;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

/// <summary>
/// The framework error endpoints (<c>MapModulusErrorPages</c>) resolve the
/// theme's <c>Errors/_{code}</c> views through <c>IModulusViewResolver</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TablerErrorPageTests
{
    [Theory]
    [InlineData(403, "Access denied")]
    [InlineData(404, "Page not found")]
    [InlineData(500, "Something went wrong")]
    public async Task Themed_error_page_is_rendered_with_the_original_status_code(int code, string title)
    {
        await using var host = await ThemeHost.StartAsync();

        var response = await host.Client.GetAsync($"/_error/{code}");
        var html = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be((HttpStatusCode)code);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        html.Should().Contain(title).And.Contain(code.ToString());
        html.Should().Contain("tabler/vendor/tabler.min.css").And.Contain("tabler/css/modulus.css");
    }

    [Fact]
    public async Task Error_pages_are_standalone_so_they_survive_a_broken_shell()
    {
        await using var host = await ThemeHost.StartAsync();

        var html = await (await host.Client.GetAsync("/_error/404")).Content.ReadAsStringAsync();

        html.Should().NotContain("navbar-vertical").And.NotContain("m-topbar");
        html.Should().NotContain("<script", "error pages must not depend on the client runtime");
    }

    [Fact]
    public async Task Codes_without_a_themed_view_fall_back_to_plain_text_with_that_status()
    {
        await using var host = await ThemeHost.StartAsync();

        var response = await host.Client.GetAsync("/_error/418");

        response.StatusCode.Should().Be((HttpStatusCode)418);
        (await response.Content.ReadAsStringAsync()).Should().Be("Error 418");
    }
}
