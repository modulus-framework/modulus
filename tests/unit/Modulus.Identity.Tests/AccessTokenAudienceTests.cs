using System.Net;
using FluentAssertions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// <c>Identity:Audiences</c> puts the configured audience in the <c>aud</c> claim of every access token the server issues, so a resource
/// server that checks its audience (for example the provisioning host) can accept them.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AccessTokenAudienceTests : OidcHostTestBase
{
    protected override void ConfigureSettings(Dictionary<string, string?> settings)
    {
        base.ConfigureSettings(settings);
        settings["Identity:Audiences:0"] = "api://shop";
    }

    [Fact]
    public async Task The_access_token_from_a_password_sign_in_carries_the_configured_audience()
    {
        await CreateUserAsync("ann");

        var (status, body) = await PasswordAsync("ann");

        status.Should().Be(HttpStatusCode.OK, body.ToString());
        Jwt(body).Audiences.Should().Equal("api://shop");
    }
}
