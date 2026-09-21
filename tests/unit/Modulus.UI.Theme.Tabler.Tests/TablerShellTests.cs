using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Modulus.UI;
using Modulus.UI.Theming;
using Modulus.UI.Theming.Tabler;
using Xunit;

namespace Modulus.UI.Theme.Tabler.Tests;

[Trait("Category", "Unit")]
public sealed class TablerShellTests
{
    private static HttpRequest Request(string path = "/", string? cookie = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        if (cookie is not null)
        {
            http.Request.Headers.Cookie = $"{TablerShell.ColorModeCookie}={cookie}";
        }

        return http.Request;
    }

    [Theory]
    [InlineData("light", "light")]
    [InlineData("dark", "dark")]
    [InlineData("DARK", "dark")]
    [InlineData("system", "light")]
    [InlineData("nonsense", "light")]
    public void Configured_mode_is_used_when_the_user_may_not_switch(string configured, string expected)
    {
        var options = new ThemeOptions { ColorMode = configured };

        TablerShell.ResolveColorMode(Request(cookie: "dark"), options).Should().Be(expected);
    }

    [Fact]
    public void User_cookie_wins_only_when_switching_is_allowed()
    {
        var options = new ThemeOptions { ColorMode = "light", AllowUserThemeSwitch = true };

        TablerShell.ResolveColorMode(Request(cookie: "dark"), options).Should().Be("dark");
        TablerShell.ResolveColorMode(Request(), options).Should().Be("light");
        TablerShell.ResolveColorMode(Request(cookie: "garbage"), options).Should().Be("light");
    }

    [Fact]
    public void System_mode_renders_light_and_is_flagged_for_the_client_to_resolve()
    {
        var options = new ThemeOptions { ColorMode = "system" };

        TablerShell.ResolveColorMode(Request(), options).Should().Be("light");
        TablerShell.FollowsSystem(Request(), options).Should().BeTrue();
        TablerShell.FollowsSystem(Request(), new ThemeOptions { ColorMode = "dark" }).Should().BeFalse();
    }

    [Theory]
    [InlineData("/users", "/users", true)]
    [InlineData("/users/5", "/users", true)]
    [InlineData("/USERS/5", "/users", true)]
    [InlineData("/users-archive", "/users", false)]
    [InlineData("/", "/", true)]
    [InlineData("/users", "/", false)]
    [InlineData("/users", "~/users", true)]
    [InlineData("/users", "/users?tab=1", true)]
    [InlineData("/users", "/users/", true)]
    [InlineData("/users", "#", false)]
    [InlineData("/users", "", false)]
    public void IsActive_matches_on_path_segments(string requestPath, string url, bool expected)
    {
        var item = new UiMenuItem("x", "X", url);

        TablerShell.IsActive(Request(requestPath), item).Should().Be(expected);
    }

    [Fact]
    public void IsActive_marks_a_group_active_when_any_child_matches()
    {
        var group = new UiMenuItem("g", "G", "#", Children:
        [
            new UiMenuItem("a", "A", "/a"),
            new UiMenuItem("b", "B", "/b"),
        ]);

        TablerShell.IsActive(Request("/b/1"), group).Should().BeTrue();
        TablerShell.IsActive(Request("/c"), group).Should().BeFalse();
    }
}
