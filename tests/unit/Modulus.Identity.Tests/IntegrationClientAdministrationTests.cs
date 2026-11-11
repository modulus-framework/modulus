using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// The integration client routes over real HTTP: a platform operator reads, binds and disables a partner client; anyone else is
/// refused, and every change is audited.
/// </summary>
[Trait("Category", "Unit")]
public sealed class IntegrationClientAdministrationTests : OidcHostTestBase
{
    private const string Operator = "operator";
    private static readonly Guid s_other = Guid.Parse("44444444-4444-4444-4444-444444444444");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddModulusIntegrationClientAdministration();
        // The Treasurer role (seeded by the base host) holds the administration permission; step-up is a separate concern, tested elsewhere.
        services.AddPermissionGrants(grants => grants.GrantToRole("Treasurer", IntegrationClientAdministrationExtensions.ManagePermission));
        services.PostConfigure<ModulusAuthorizationOptions>(o => o.StepUp.RequireForCritical = false);
    }

    protected override void ConfigureEndpoints(WebApplication app) => app.MapModulusIntegrationClients();

    private async Task<string> TokenOfAsync(string user)
    {
        var (status, body) = await PasswordAsync(user);
        status.Should().Be(HttpStatusCode.OK, body.ToString());
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? bearer, object? body = null)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(method, path);
        if (bearer is not null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private async Task<JsonElement> ReadBindingAsync(string clientId, string bearer)
    {
        var response = await SendAsync(HttpMethod.Get, $"/identity/integration-clients/{clientId}", bearer);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task Anonymous_callers_are_refused()
    {
        var response = await SendAsync(HttpMethod.Get, $"/identity/integration-clients/{Tablet}", bearer: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_signed_in_user_without_the_permission_is_forbidden()
    {
        await CreateUserAsync("bob");
        var bearer = await TokenOfAsync("bob");

        var response = await SendAsync(HttpMethod.Get, $"/identity/integration-clients/{Tablet}", bearer);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_operator_reads_the_binding_of_a_client()
    {
        await CreateUserAsync(Operator, "Treasurer");
        var bearer = await TokenOfAsync(Operator);

        var binding = await ReadBindingAsync(Tablet, bearer);

        binding.GetProperty("tenantId").GetGuid().Should().Be(s_company, "the base host binds the tablet to the test company");
    }

    [Fact]
    public async Task An_unknown_client_is_not_found()
    {
        await CreateUserAsync(Operator, "Treasurer");
        var bearer = await TokenOfAsync(Operator);

        (await SendAsync(HttpMethod.Get, "/identity/integration-clients/nobody", bearer)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Put, "/identity/integration-clients/nobody/binding", bearer, new { tenantId = s_other })).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_operator_moves_the_tablet_to_another_company_and_back_to_the_host()
    {
        await CreateUserAsync(Operator, "Treasurer");
        var bearer = await TokenOfAsync(Operator);
        Audit.Events.Clear();

        (await SendAsync(HttpMethod.Put, $"/identity/integration-clients/{Tablet}/binding", bearer, new { tenantId = s_other })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await ReadBindingAsync(Tablet, bearer)).GetProperty("tenantId").GetGuid().Should().Be(s_other);

        (await SendAsync(HttpMethod.Put, $"/identity/integration-clients/{Tablet}/binding", bearer, new { tenantId = (Guid?)null })).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await ReadBindingAsync(Tablet, bearer)).GetProperty("tenantId").ValueKind.Should().Be(JsonValueKind.Null, "null binds the host");

        Audit.Events.Should().HaveCount(2);
        Audit.Events.Should().OnlyContain(e => e.Action == "integration-client.bound" && e.Category == SecurityAuditCategories.Identity);
        Audit.Events[0].TenantId.Should().Be(s_other);
    }

    [Fact]
    public async Task Disabling_a_client_ends_it_now_and_is_audited()
    {
        await CreateUserAsync(Operator, "Treasurer");
        var bearer = await TokenOfAsync(Operator);
        Audit.Events.Clear();

        (await SendAsync(HttpMethod.Post, $"/identity/integration-clients/{Tablet}/disable", bearer)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var binding = await ReadBindingAsync(Tablet, bearer);
        binding.GetProperty("validUntil").ValueKind.Should().NotBe(JsonValueKind.Null, "a disabled client has an end date in the past");
        Audit.Events.Should().ContainSingle(e => e.Action == "integration-client.disabled" && e.Target == $"client:{Tablet}");
    }
}
