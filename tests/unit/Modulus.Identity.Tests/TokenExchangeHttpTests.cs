using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using OpenIddict.Abstractions;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Modulus.Identity.Tests;

/// <summary>
/// Token exchange (spec 12A) over real HTTP: an agent acts for a signed-in person, the issued token keeps the person as subject and
/// names the agent in <c>act</c>, and each refusal is an <c>invalid_grant</c>.
/// </summary>
[Trait("Category", "Unit")]
public sealed class TokenExchangeHttpTests : OidcHostTestBase
{
    private const string AgentClient = "agent-1";
    private const string AgentSecret = "agent-secret-for-tests";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    private async Task<ModulusUser> UserInCompanyAsync(string name, Guid company)
    {
        var user = await CreateUserAsync(name);
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
        stored.TenantId = company;
        (await users.UpdateAsync(stored)).Succeeded.Should().BeTrue();
        return stored;
    }

    private async Task AgentAsync(Guid? company, bool mayActForPeople)
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
        (await directory.BindAsync(AgentClient, company, null)).Should().BeTrue();
        (await directory.SetMayActForPeopleAsync(AgentClient, mayActForPeople)).Should().BeTrue();
    }

    private async Task<string> ActorTokenAsync()
    {
        var (status, body) = await TokenAsync(
            ("grant_type", "client_credentials"), ("client_id", AgentClient), ("client_secret", AgentSecret), ("scope", "openid"));
        status.Should().Be(HttpStatusCode.OK, body.ToString());
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<string> PersonTokenAsync(string name)
    {
        var (status, body) = await PasswordAsync(name);
        status.Should().Be(HttpStatusCode.OK, body.ToString());
        return body.GetProperty("access_token").GetString()!;
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> ExchangeAsync(string subject, string actor)
        => TokenAsync(
            ("grant_type", TokenExchangeGrant.GrantType), ("client_id", AgentClient), ("client_secret", AgentSecret),
            ("subject_token", subject), ("subject_token_type", AccessTokenType),
            ("actor_token", actor), ("actor_token_type", AccessTokenType), ("scope", "openid"));

    [Fact]
    public async Task A_person_exchanged_for_an_agent_gets_a_token_for_that_person_naming_the_agent_with_a_short_life()
    {
        var ann = await UserInCompanyAsync("ann", s_company);
        await AgentAsync(s_company, mayActForPeople: true);

        var (status, body) = await ExchangeAsync(await PersonTokenAsync("ann"), await ActorTokenAsync());

        status.Should().Be(HttpStatusCode.OK, body.ToString());
        body.TryGetProperty("refresh_token", out _).Should().BeFalse("an exchanged token is never refreshed");
        var jwt = Jwt(body);
        jwt.GetClaim("sub").Value.Should().Be(ann.Id.ToString());
        var success = Audit.Events.Should().ContainSingle(e => e.Action == "token.exchange").Subject;
        success.Outcome.Should().Be(SecurityAuditOutcomes.Success);
        success.Actor.Should().Be(ann.Id.ToString());
        success.Details["client"].Should().Be(AgentClient, "the audit names the agent");
        jwt.GetClaim("act").Value.Should().Be(IntegrationClients.RoleFor(AgentClient));
        var issuedFor = long.Parse(jwt.GetClaim("exp").Value, System.Globalization.CultureInfo.InvariantCulture)
            - long.Parse(jwt.GetClaim("iat").Value, System.Globalization.CultureInfo.InvariantCulture);
        issuedFor.Should().BeLessThanOrEqualTo(TokenExchangeGrant.MaxLifetimeMinutes * 60 + 60, "the lifetime is capped at the spec's 15 minutes");
    }

    [Fact]
    public async Task An_agent_that_may_not_act_for_people_is_refused()
    {
        await UserInCompanyAsync("ann", s_company);
        await AgentAsync(s_company, mayActForPeople: false);

        var (status, body) = await ExchangeAsync(await PersonTokenAsync("ann"), await ActorTokenAsync());

        status.Should().Be(HttpStatusCode.BadRequest, "a refused grant is invalid_grant, a 400");
        body.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task A_person_of_another_company_is_refused()
    {
        await UserInCompanyAsync("ann", Guid.Parse("44444444-4444-4444-4444-444444444444"));
        await AgentAsync(s_company, mayActForPeople: true);

        var (status, body) = await ExchangeAsync(await PersonTokenAsync("ann"), await ActorTokenAsync());

        status.Should().Be(HttpStatusCode.BadRequest, "a refused grant is invalid_grant, a 400");
        body.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task A_token_issued_through_an_agent_cannot_be_exchanged_again()
    {
        await UserInCompanyAsync("ann", s_company);
        await AgentAsync(s_company, mayActForPeople: true);
        var (_, issued) = await ExchangeAsync(await PersonTokenAsync("ann"), await ActorTokenAsync());
        var viaAgent = issued.GetProperty("access_token").GetString()!;

        var (status, body) = await ExchangeAsync(viaAgent, await ActorTokenAsync());

        status.Should().Be(HttpStatusCode.BadRequest, "a refused grant is invalid_grant, a 400");
        body.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task A_person_who_may_no_longer_start_access_cannot_be_acted_for()
    {
        var ann = await UserInCompanyAsync("ann", s_company);
        await AgentAsync(s_company, mayActForPeople: true);
        var personToken = await PersonTokenAsync("ann");
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var stored = (await users.FindByIdAsync(ann.Id.ToString()))!;
            stored.Lifecycle = AccountLifecycle.Disabled;
            (await users.UpdateAsync(stored)).Succeeded.Should().BeTrue();
        }

        var (status, body) = await ExchangeAsync(personToken, await ActorTokenAsync());

        status.Should().Be(HttpStatusCode.BadRequest, "a refused grant is invalid_grant, a 400");
        body.GetProperty("error").GetString().Should().Be("invalid_grant");
    }

    [Fact]
    public async Task Every_refusal_is_audited_as_denied_and_names_the_agent_when_there_is_one()
    {
        await UserInCompanyAsync("ann", s_company);
        await AgentAsync(s_company, mayActForPeople: false);

        await ExchangeAsync(await PersonTokenAsync("ann"), await ActorTokenAsync());

        var denied = Audit.Events.Should().ContainSingle(e => e.Action == "token.exchange").Subject;
        denied.Outcome.Should().Be(SecurityAuditOutcomes.Denied);
        denied.Details["client"].Should().Be(AgentClient);
        denied.Actor.Should().BeNull("the refusal is recorded before the person is trusted");
    }

    [Fact]
    public async Task A_missing_actor_token_is_an_invalid_request()
    {
        await UserInCompanyAsync("ann", s_company);
        await AgentAsync(s_company, mayActForPeople: true);

        var (status, body) = await TokenAsync(
            ("grant_type", TokenExchangeGrant.GrantType), ("client_id", AgentClient), ("client_secret", AgentSecret),
            ("subject_token", await PersonTokenAsync("ann")), ("subject_token_type", AccessTokenType));

        status.Should().Be(HttpStatusCode.BadRequest, "a refused grant is invalid_grant, a 400");
        body.GetProperty("error").GetString().Should().Be("invalid_request");
    }
}
