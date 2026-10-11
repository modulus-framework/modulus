using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>The device authorization grant (RFC 8628): a keyboard-less device shows a code, a signed-in person approves it with their bearer token.</summary>
[Trait("Category", "Unit")]
public sealed class DeviceCodeHttpTests : OidcHostTestBase
{
    private const string DeviceGrant = "urn:ietf:params:oauth:grant-type:device_code";

    protected override void ConfigureSettings(Dictionary<string, string?> settings)
    {
        settings["Identity:Sessions:Enabled"] = "true";
        settings["Identity:ValidateTokenEntries"] = "false";
        settings["Identity:AccessTokenLifetimeMin"] = "1440";
        settings["Identity:AccessTokenLifetimeMin"] = "1440";
    }

    private async Task<(string DeviceCode, string UserCode)> StartDeviceAsync(string client = SpaClient)
    {
        using var http = NewClient();
        var response = await http.PostAsync("/connect/device", Form(("client_id", client), ("scope", "openid offline_access roles")));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        response.StatusCode.Should().Be(HttpStatusCode.OK, body.ToString());
        body.GetProperty("verification_uri").GetString().Should().EndWith("/connect/verify");
        return (body.GetProperty("device_code").GetString()!, body.GetProperty("user_code").GetString()!);
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> PollAsync(string deviceCode)
        => TokenAsync(("grant_type", DeviceGrant), ("client_id", SpaClient), ("device_code", deviceCode));

    private async Task<HttpResponseMessage> VerifyAsync(string? bearer, string userCode, string? decision, HttpMethod? method = null)
    {
        using var http = NewClient();
        var post = (method ?? HttpMethod.Post) == HttpMethod.Post;
        // OpenIddict reads a POST's parameters from the form only, a GET's from the query string.
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, post ? "/connect/verify" : $"/connect/verify?user_code={Uri.EscapeDataString(userCode)}");
        if (bearer is not null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        if (post)
            request.Content = Form(("user_code", userCode), ("decision", decision ?? ""));
        return await http.SendAsync(request);
    }

    [Fact]
    public async Task A_device_gets_tokens_once_a_signed_in_person_approves_its_code()
    {
        await CreateUserAsync("ann", "Planner");
        var (_, phone) = await PasswordAsync("ann");
        var phoneToken = Jwt(phone);
        var (deviceCode, userCode) = await StartDeviceAsync();

        // Until someone approves, the device keeps polling.
        var (pendingStatus, pending) = await PollAsync(deviceCode);
        pendingStatus.Should().Be(HttpStatusCode.BadRequest);
        pending.GetProperty("error").GetString().Should().Be("authorization_pending");

        // The approving app can first show what is being authorized.
        var preview = await VerifyAsync(phone.GetProperty("access_token").GetString(), userCode, null, HttpMethod.Get);
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var shown = JsonDocument.Parse(await preview.Content.ReadAsStringAsync()).RootElement;
        shown.GetProperty("client_id").GetString().Should().Be(SpaClient);
        shown.GetProperty("scopes").EnumerateArray().Select(x => x.GetString()).Should().Contain("openid");

        var approved = await VerifyAsync(phone.GetProperty("access_token").GetString(), userCode, "approve");
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());

        var (status, tokens) = await PollAsync(deviceCode);
        status.Should().Be(HttpStatusCode.OK, tokens.ToString());
        var jwt = Jwt(tokens);
        jwt.GetClaim("sub").Value.Should().Be(phoneToken.GetClaim("sub").Value);
        Amr(jwt).Should().Equal(["pwd"], "the device carries the strength of the approver's sign-in");
        jwt.GetClaim("auth_time").Value.Should().Be(phoneToken.GetClaim("auth_time").Value);
        jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).Should().Contain("Planner");
        tokens.TryGetProperty("refresh_token", out _).Should().BeTrue();
        (await CallAsync(HttpMethod.Get, "/whoami", tokens.GetProperty("access_token").GetString())).StatusCode.Should().Be(HttpStatusCode.OK);

        // The device has a session of its own: ending the phone's does not sign the device out, and the reverse.
        jwt.GetClaim("sid").Value.Should().NotBe(phoneToken.GetClaim("sid").Value);

        // A code works once.
        (await VerifyAsync(phone.GetProperty("access_token").GetString(), userCode, "approve")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PollAsync(deviceCode)).Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Approving_needs_a_bearer_token_a_real_code_and_a_recent_sign_in()
    {
        await CreateUserAsync("ann");
        var (_, phone) = await PasswordAsync("ann");
        var bearer = phone.GetProperty("access_token").GetString();
        var (_, userCode) = await StartDeviceAsync();

        var anonymous = await VerifyAsync(null, userCode, "approve");
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var wrong = await VerifyAsync(bearer, "ZZZZ-ZZZZ", "approve");
        wrong.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("invalid_user_code");

        (await VerifyAsync(bearer, userCode, "maybe")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The approver signed in more than 15 minutes ago: refused until they sign in again.
        Clock.Advance(TimeSpan.FromMinutes(16));
        var (_, lateCode) = await StartDeviceAsync();
        var stale = await VerifyAsync(bearer, lateCode, "approve");
        stale.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await stale.Content.ReadAsStringAsync()).Should().Contain("recent_sign_in_required");

        var fresh = await VerifyAsync(PasswordAsyncToken(await PasswordAsync("ann")), lateCode, "approve");
        fresh.StatusCode.Should().Be(HttpStatusCode.OK, "a fresh sign-in is enough");
    }

    private static string PasswordAsyncToken((HttpStatusCode Status, JsonElement Body) result) => result.Body.GetProperty("access_token").GetString()!;

    [Fact]
    public async Task A_refused_device_is_told_access_denied()
    {
        await CreateUserAsync("ann");
        var bearer = PasswordAsyncToken(await PasswordAsync("ann"));
        var (deviceCode, userCode) = await StartDeviceAsync();

        await VerifyAsync(bearer, userCode, "deny");

        var (status, body) = await PollAsync(deviceCode);
        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetString().Should().Be("access_denied");
    }

    [Fact]
    public async Task A_client_without_the_device_permission_cannot_start_the_flow()
    {
        using var http = NewClient();
        var response = await http.PostAsync("/connect/device", Form(("client_id", "limited"), ("scope", "openid")));
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
        var text = await response.Content.ReadAsStringAsync();
        (text.Contains("unauthorized_client", StringComparison.Ordinal) || text.Contains("invalid_client", StringComparison.Ordinal)).Should().BeTrue(text);
    }
}
