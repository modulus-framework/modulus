namespace Modulus.AI.Connector.Tests;

using System.Net;
using System.Security.Cryptography;
using FluentAssertions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class ConnectorAuthenticationTests
{
    private const string Search = "/capabilities/Test.Catalog.Product.Search:execute";

    private static string? Reason(Modulus.Core.Abstractions.SecurityAuditEvent e)
        => e.Details is not null && e.Details.TryGetValue("reason", out var reason) ? reason : null;

    [Fact]
    public async Task Manifest_needs_only_the_api_key()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var response = await host.Client.SendAsync(host.Request(HttpMethod.Get, "/manifest", envelope: null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Missing_or_unknown_api_key_is_denied(string? apiKey)
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var manifest = await host.Client.SendAsync(host.Request(HttpMethod.Get, "/manifest", null, apiKey: apiKey));
        var search = await host.Client.SendAsync(host.Request(HttpMethod.Post, Search, host.Envelope(), "{}", apiKey));

        manifest.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        search.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await search.Content.ReadAsStringAsync()).Should().Contain("\"DENIED\"");
    }

    [Fact]
    public async Task A_browser_origin_is_refused()
    {
        await using var host = await ConnectorTestHost.StartAsync();
        var request = host.Request(HttpMethod.Post, Search, host.Envelope(), "{}");
        request.Headers.Add("Origin", "https://evil.test");

        var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Audit.Events.Should().Contain(e => Reason(e) == "browser origin");
    }

    [Fact]
    public async Task A_user_call_without_an_envelope_is_denied()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var response = await host.Client.SendAsync(host.Request(HttpMethod.Post, Search, envelope: null, "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_expired_envelope_is_denied()
    {
        await using var host = await ConnectorTestHost.StartAsync();
        var envelope = host.Envelope(tweak: d =>
        {
            d.IssuedAt = DateTime.UtcNow.AddMinutes(-3);
            d.NotBefore = d.IssuedAt;
            d.Expires = DateTime.UtcNow.AddMinutes(-2);
        });

        var response = await host.PostAsync(Search, "{}", envelope);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_envelope_living_too_long_is_denied()
    {
        await using var host = await ConnectorTestHost.StartAsync();
        var envelope = host.Envelope(tweak: d => d.Expires = DateTime.UtcNow.AddHours(1));

        var response = await host.PostAsync(Search, "{}", envelope);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Audit.Events.Should().Contain(e => Reason(e) == "envelope lifetime too long");
    }

    [Fact]
    public async Task An_envelope_signed_by_another_key_is_denied()
    {
        await using var host = await ConnectorTestHost.StartAsync();
        using var other = RSA.Create(2048);

        var response = await host.PostAsync(Search, "{}", host.Envelope(key: other));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_envelope_for_an_unknown_audience_is_denied()
    {
        await using var host = await ConnectorTestHost.StartAsync();
        var envelope = host.Envelope(tweak: d => d.Audience = "someone-else");

        var response = await host.PostAsync(Search, "{}", envelope);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_envelope_of_another_platform_tenant_is_denied()
    {
        await using var host = await ConnectorTestHost.StartAsync();
        var envelope = host.Envelope(tweak: d => d.Claims[AiEnvelopeClaims.TenantId] = "pt-other");

        var response = await host.PostAsync(Search, "{}", envelope);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Audit.Events.Should().Contain(e => Reason(e) == "app instance not mapped to this platform tenant");
    }

    [Fact]
    public async Task A_replayed_envelope_is_denied()
    {
        await using var host = await ConnectorTestHost.StartAsync();
        var envelope = host.Envelope();

        var first = await host.PostAsync(Search, "{}", envelope);
        var second = await host.PostAsync(Search, "{}", envelope);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Audit.Events.Should().Contain(e => Reason(e) == "envelope replayed");
    }

    [Fact]
    public async Task An_unknown_user_is_denied()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var response = await host.PostAsync(Search, "{}", host.Envelope(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Every_user_is_denied_until_a_resolver_is_chosen()
    {
        await using var host = await ConnectorTestHost.StartAsync(resolveUsers: false);

        var response = await host.PostAsync(Search, "{}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_company_instance_needs_the_users_membership()
    {
        await using var host = await ConnectorTestHost.StartAsync();

        var outsider = await host.PostAsync(Search, "{}", host.Envelope(instance: ConnectorTestHost.CompanyInstance));
        host.Memberships.Add(TestUsers.Reader, TestTenantRestorer.Company);
        var member = await host.PostAsync(Search, "{}", host.Envelope(instance: ConnectorTestHost.CompanyInstance));

        outsider.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        member.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Audit.Events.Should().Contain(e => Reason(e) == "not a member of the company"
            && e.TenantId == TestTenantRestorer.Company);
    }

    [Fact]
    public async Task An_unknown_company_is_denied()
    {
        await using var host = await ConnectorTestHost.StartAsync(s => s["Ai:Connector:Instances:1:TenantId"] = Guid.NewGuid().ToString());

        var response = await host.PostAsync(Search, "{}", host.Envelope(instance: ConnectorTestHost.CompanyInstance));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Disabled_connector_answers_404()
    {
        await using var host = await ConnectorTestHost.StartAsync(s => s["Ai:Connector:Enabled"] = "false");

        var response = await host.Client.SendAsync(host.Request(HttpMethod.Get, "/manifest", null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void Api_key_hash_matches_only_its_key()
    {
        var hash = AiApiKeys.Hash("k");

        AiApiKeys.Matches("k", ["not-hex", hash]).Should().BeTrue();
        AiApiKeys.Matches("k2", [hash]).Should().BeFalse();
        AiApiKeys.Matches(string.Empty, [hash]).Should().BeFalse();
    }
}
