namespace Modulus.Identity.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Identity.Abstractions;
using Xunit;

/// <summary>
/// RFC 7662 introspection over HTTP: a token is reported active only while it is a live access token. Revoked tokens, id
/// tokens and opaque refresh tokens must not come back active.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TokenIntrospectionTests : OidcHostTestBase
{
    private const string ResourceServer = "rs";
    private const string ResourceServerSecret = "rs-secret";

    protected override void ConfigureSettings(Dictionary<string, string?> settings)
    {
        settings["Identity:IntrospectionClientId"] = ResourceServer;
        settings["Identity:IntrospectionClientSecret"] = ResourceServerSecret;
    }

    [Fact]
    public async Task A_live_access_token_is_active_and_a_revoked_one_is_not()
    {
        var user = await CreateUserAsync("ann");
        var (_, body) = await PasswordAsync("ann");
        var accessToken = body.GetProperty("access_token").GetString()!;

        var live = await IntrospectAsync(accessToken);
        live.GetProperty("active").GetBoolean().Should().BeTrue(live.GetRawText());
        live.GetProperty("token_type").GetString().Should().Be("Bearer");

        // "Sign out everywhere" revokes every stored token of the account, the access token included.
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IUserSessionService>().RevokeAllAsync(user.Id, "test");

        (await IntrospectAsync(accessToken)).GetProperty("active").GetBoolean().Should().BeFalse("a revoked token is no longer in force");
    }

    [Fact]
    public async Task An_id_token_and_a_refresh_token_are_never_reported_active()
    {
        await CreateUserAsync("ann");
        var (_, body) = await PasswordAsync("ann");

        (await IntrospectAsync(body.GetProperty("refresh_token").GetString()!)).GetProperty("active").GetBoolean()
            .Should().BeFalse("a refresh token is opaque and not an access token");

        if (body.TryGetProperty("id_token", out var idToken))
            (await IntrospectAsync(idToken.GetString()!)).GetProperty("active").GetBoolean()
                .Should().BeFalse("an id token authenticates the user to the client, not to a resource");
    }

    [Fact]
    public async Task Introspection_refuses_a_caller_without_the_resource_server_credentials()
    {
        await CreateUserAsync("ann");
        var (_, body) = await PasswordAsync("ann");
        var accessToken = body.GetProperty("access_token").GetString()!;

        // A bearer end-user token is not a resource server's credential.
        using var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await client.PostAsync("/connect/introspect", Form(("token", accessToken)));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<System.Text.Json.JsonElement> IntrospectAsync(string token)
    {
        using var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ResourceServer}:{ResourceServerSecret}")));
        var response = await client.PostAsync("/connect/introspect", Form(("token", token)));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
