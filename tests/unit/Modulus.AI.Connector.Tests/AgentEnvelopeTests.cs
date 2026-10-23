namespace Modulus.AI.Connector.Tests;

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Grants;
using Microsoft.IdentityModel.Tokens;
using Xunit;

/// <summary>A9-1 on the AI path: an envelope may name the agent acting for the user; only an integration role is accepted, and the user's access is capped to it.</summary>
[Trait("Category", "Unit")]
public sealed class AgentEnvelopeTests
{
    private const string Search = "/capabilities/Test.Catalog.Product.Search:execute";

    private static void Agent(string role, SecurityTokenDescriptor d) => d.Claims![AiEnvelopeClaims.Agent] = role;

    [Fact]
    public async Task A_call_without_an_agent_is_answered_as_before()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var response = await host.Client.SendAsync(host.Request(HttpMethod.Post, Search, host.Envelope(), "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("\"DENIED\"");
    }

    [Fact]
    public async Task An_agent_that_is_not_an_integration_role_is_refused_at_the_door()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var response = await host.Client.SendAsync(host.Request(HttpMethod.Post, Search, host.Envelope(tweak: d => Agent("admin", d)), "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Audit.Events.Should().Contain(e => e.Details != null && e.Details.Values.Contains("agent claim is not an integration role"));
    }

    [Fact]
    public async Task An_agent_without_grants_holds_nothing_even_though_the_user_may_read()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var response = await host.Client.SendAsync(host.Request(
            HttpMethod.Post, Search, host.Envelope(tweak: d => Agent("integration:tablet-1", d)), "{}"));

        (await response.Content.ReadAsStringAsync()).Should().Contain("\"DENIED\"",
            "the user alone can read the product, but an agent acting for them holds nothing without grants (the cap)");
    }

    [Fact]
    public async Task An_agent_granted_the_permission_reads_through_the_real_grant_store()
    {
        await using var host = await ConnectorTestHost.StartAsync();
        host.Services.GetRequiredService<InMemoryPermissionGrantStore>().GrantToRole("integration:tablet-1", "catalog:read");

        var response = await host.Client.SendAsync(host.Request(
            HttpMethod.Post, Search, host.Envelope(tweak: d => Agent("integration:tablet-1", d)), "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("\"DENIED\"");
    }

    [Fact]
    public async Task An_agent_granted_only_another_permission_cannot_read_the_product()
    {
        await using var host = await ConnectorTestHost.StartAsync();
        host.Services.GetRequiredService<InMemoryPermissionGrantStore>().GrantToRole("integration:tablet-1", "audit:view");

        var response = await host.Client.SendAsync(host.Request(
            HttpMethod.Post, Search, host.Envelope(tweak: d => Agent("integration:tablet-1", d)), "{}"));

        (await response.Content.ReadAsStringAsync()).Should().Contain("\"DENIED\"", "the agent's grant is the cap");
    }

    [Fact]
    public async Task An_agent_granted_the_permission_does_not_widen_a_user_who_lacks_it()
    {
        await using var host = await ConnectorTestHost.StartAsync();
        host.Services.GetRequiredService<InMemoryPermissionGrantStore>().GrantToRole("integration:tablet-1", "catalog:read");

        var response = await host.Client.SendAsync(host.Request(
            HttpMethod.Post, Search, host.Envelope(user: TestUsers.NoRights, tweak: d => Agent("integration:tablet-1", d)), "{}"));

        (await response.Content.ReadAsStringAsync()).Should().Contain("\"DENIED\"", "an agent never gives the user more than the user has");
    }
}
