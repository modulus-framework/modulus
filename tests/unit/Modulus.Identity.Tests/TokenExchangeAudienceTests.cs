using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using OpenIddict.Abstractions;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Modulus.Identity.Tests;

/// <summary>
/// With <c>Identity:Audiences</c> configured, token exchange only accepts a person's token and an agent's token issued for this API:
/// a token that names another audience is refused, even when it is genuine and in force.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TokenExchangeAudienceTests : OidcHostTestBase
{
    private const string ThisApi = "api://shop";
    private const string AgentClient = "agent-aud";
    private const string AgentSecret = "agent-aud-secret";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    protected override void ConfigureSettings(Dictionary<string, string?> settings)
    {
        base.ConfigureSettings(settings);
        settings["Identity:Audiences:0"] = ThisApi;
        // OpenIddict only lets a client exchange a subject token whose audience names that client, so the agent is one of them.
        settings["Identity:Audiences:1"] = AgentClient;
        settings["Identity:TokenExchange:Enabled"] = "true";
    }

    private async Task AgentAsync()
    {
        using var scope = _app.Services.CreateScope();
        var apps = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = AgentClient,
            ClientSecret = AgentSecret,
            ClientType = ClientTypes.Confidential,
        };
        descriptor.Permissions.UnionWith(
        [
            Permissions.Endpoints.Token,
            Permissions.GrantTypes.ClientCredentials,
            Permissions.Prefixes.GrantType + TokenExchangeGrant.GrantType,
            Permissions.Prefixes.Scope + Scopes.OpenId,
        ]);
        await apps.CreateAsync(descriptor);

        var directory = scope.ServiceProvider.GetRequiredService<IIntegrationClientDirectory>();
        (await directory.BindAsync(AgentClient, s_company, null)).Should().BeTrue();
        (await directory.SetMayActForPeopleAsync(AgentClient, true)).Should().BeTrue();
    }

    private async Task<string> PersonAsync(string name)
    {
        var user = await CreateUserAsync(name);
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
            stored.TenantId = s_company;
            (await users.UpdateAsync(stored)).Succeeded.Should().BeTrue();
        }
        var (status, body) = await PasswordAsync(name);
        status.Should().Be(HttpStatusCode.OK, body.ToString());
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<string> ActorAsync()
    {
        var (status, body) = await TokenAsync(
            ("grant_type", "client_credentials"), ("client_id", AgentClient), ("client_secret", AgentSecret), ("scope", "openid"));
        status.Should().Be(HttpStatusCode.OK, body.ToString());
        return body.GetProperty("access_token").GetString()!;
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> ExchangeAsync(string subject, string actor)
        => TokenAsync(
            ("grant_type", TokenExchangeGrant.GrantType), ("client_id", AgentClient), ("client_secret", AgentSecret),
            ("subject_token", subject), ("subject_token_type", AccessTokenType),
            ("actor_token", actor), ("actor_token_type", AccessTokenType), ("scope", "openid"));

    [Fact]
    public async Task Tokens_for_the_configured_audience_are_exchanged_and_the_result_names_it()
    {
        await AgentAsync();
        var person = await PersonAsync("ann");
        var actor = await ActorAsync();

        var (status, body) = await ExchangeAsync(person, actor);

        status.Should().Be(HttpStatusCode.OK, body.ToString());
        Jwt(body).Audiences.Should().Contain(ThisApi);
    }

    [Fact]
    public async Task A_person_token_for_another_audience_is_refused_even_though_it_is_genuine_and_in_force()
    {
        await AgentAsync();
        var person = await PersonAsync("ann");
        var actor = await ActorAsync();
        // The genuine tokens name ThisApi. Moving this server's expectation to another audience makes both of them foreign.
        _app.Services.GetRequiredService<IOptions<ModulusIdentityOptions>>().Value.Audiences = ["api://other"];

        var (status, body) = await ExchangeAsync(person, actor);

        status.Should().Be(HttpStatusCode.BadRequest, body.ToString());
        body.GetProperty("error").GetString().Should().Be("invalid_grant");
    }
}
