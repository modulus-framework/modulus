using FluentAssertions;
using Modulus.Cli.Commands;
using Xunit;

namespace Modulus.Cli.Tests;

/// <summary>
/// The CLI creates APIs only. The parser ignores unknown options, so the removed <c>--kind</c> and <c>--ui-engine</c> are refused
/// explicitly: a script that asks for a web app must not silently get an API.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RetiredOptionsTests
{
    private static ILookup<string, string?> Options(string name, string? value)
        => new[] { (name, value) }.ToLookup(o => o.name, o => o.value);

    [Theory]
    [InlineData("--kind", "webapp+api")]
    [InlineData("--kind", "web")]
    [InlineData("kind", "webapp")]
    [InlineData("--ui-engine", "mvc")]
    [InlineData("--ui-engine", null)]
    public void A_request_for_a_ui_is_refused(string name, string? value)
        => FluentActions.Invoking(() => NewAppCommand.RefuseRetiredOptions(Options(name, value)))
            .Should().Throw<ArgumentException>().WithMessage("*creates APIs only*");

    [Theory]
    [InlineData("--kind", "api")]
    [InlineData("--ui-engine", "none")]
    [InlineData("--something-else", "mvc")]
    public void An_option_that_asks_for_no_ui_is_accepted(string name, string value)
        => FluentActions.Invoking(() => NewAppCommand.RefuseRetiredOptions(Options(name, value))).Should().NotThrow();

    [Fact]
    public void Auth_none_warns_that_no_scheme_is_registered()
        => NewAppCommand.AuthNote("none").Should().Contain("no authentication scheme");

    [Fact]
    public void The_local_token_server_note_explains_the_development_sign_in()
    {
        NewAppCommand.AuthNote("openiddict").Should().Contain("bearer token").And.Contain("Before production");
        NewAppCommand.AuthNote("keycloak").Should().BeNull();
    }
}
