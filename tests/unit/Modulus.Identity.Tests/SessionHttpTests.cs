using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>Sign-in sessions over HTTP: recorded metadata, the concurrent-session cap, per-role idle timeouts and ending one session.</summary>
[Trait("Category", "Unit")]
public sealed class SessionHttpTests : OidcHostTestBase
{
    protected override void ConfigureSettings(Dictionary<string, string?> settings)
    {
        settings["Identity:Sessions:Enabled"] = "true";
        settings["Identity:Sessions:MaxConcurrent"] = "3";
        settings["Identity:Sessions:MaxConcurrentByRole:Treasurer"] = "1";
        settings["Identity:Sessions:IdleTimeoutMinutesByRole:Treasurer"] = "15";
        settings["Identity:AccessTokenLifetimeMin"] = "1440";
        settings["Identity:ValidateTokenEntries"] = "false";
    }

    private async Task<(string Access, string Refresh, string Sid)> SignInAsync(string user, string? device = null, string? userAgent = null)
    {
        using var client = NewClient();
        if (userAgent is not null)
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        var fields = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "password"), new("client_id", SpaClient), new("username", user), new("password", Password),
            new("scope", "openid offline_access roles"),
        };
        if (device is not null)
            fields.Add(new("device_name", device));
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(fields));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        response.StatusCode.Should().Be(HttpStatusCode.OK, body.ToString());
        var access = body.GetProperty("access_token").GetString()!;
        return (access, body.GetProperty("refresh_token").GetString()!, Jwt(body).GetClaim("sid").Value);
    }

    private async Task<HttpStatusCode> WhoAmIAsync(string access) => (await CallAsync(HttpMethod.Get, "/whoami", access)).StatusCode;

    private async Task<HttpStatusCode> RefreshAsync(string refresh)
        => (await TokenAsync(("grant_type", "refresh_token"), ("client_id", SpaClient), ("refresh_token", refresh))).Status;

    private async Task<JsonElement[]> ListAsync(string access)
    {
        using var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        var response = await client.GetAsync("/account/sign-ins");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement[]>())!;
    }

    [Fact]
    public async Task A_sign_in_is_recorded_with_where_it_came_from_and_listed_to_its_owner()
    {
        await CreateUserAsync("ann");

        var (access, _, sid) = await SignInAsync("ann", device: "Front desk", userAgent: "ErpApp/2.1 (Android 14)");

        var sessions = await ListAsync(access);
        var session = sessions.Should().ContainSingle().Subject;
        session.GetProperty("id").GetString().Should().Be(sid);
        session.GetProperty("deviceName").GetString().Should().Be("Front desk");
        session.GetProperty("userAgent").GetString().Should().Contain("ErpApp/2.1");
        session.GetProperty("clientId").GetString().Should().Be(SpaClient);
        session.GetProperty("methods")[0].GetString().Should().Be("pwd");
        session.GetProperty("current").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task At_the_limit_the_least_recently_used_session_ends_and_its_tokens_stop_working()
    {
        await CreateUserAsync("ann");
        var first = await SignInAsync("ann");
        Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await SignInAsync("ann");
        Clock.Advance(TimeSpan.FromMinutes(1));
        var third = await SignInAsync("ann");
        (await WhoAmIAsync(first.Access)).Should().Be(HttpStatusCode.OK, "three sessions are allowed");

        Clock.Advance(TimeSpan.FromMinutes(1));
        var fourth = await SignInAsync("ann");

        // The first was used last (the whoami above), so the second is the one used least recently.
        (await WhoAmIAsync(second.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(second.Refresh)).Should().Be(HttpStatusCode.BadRequest);
        (await WhoAmIAsync(third.Access)).Should().Be(HttpStatusCode.OK);
        (await WhoAmIAsync(fourth.Access)).Should().Be(HttpStatusCode.OK);
        (await ListAsync(fourth.Access)).Should().HaveCount(3);
    }

    [Fact]
    public async Task A_role_can_lower_the_limit_below_the_global_one()
    {
        await CreateUserAsync("tess", "Treasurer");
        await CreateUserAsync("pam", "Planner");

        var firstTreasurer = await SignInAsync("tess");
        await SignInAsync("tess");
        (await WhoAmIAsync(firstTreasurer.Access)).Should().Be(HttpStatusCode.Unauthorized, "the treasurer role allows one session");

        var firstPlanner = await SignInAsync("pam");
        await SignInAsync("pam");
        (await WhoAmIAsync(firstPlanner.Access)).Should().Be(HttpStatusCode.OK, "the global limit of three applies to a planner");
    }

    [Fact]
    public async Task A_role_can_time_out_sooner_and_activity_keeps_a_session_alive()
    {
        await CreateUserAsync("tess", "Treasurer");
        await CreateUserAsync("pam", "Planner");
        var treasurer = await SignInAsync("tess");
        var planner = await SignInAsync("pam");

        // Used every ten minutes: never idle for fifteen.
        for (var i = 0; i < 4; i++)
        {
            Clock.Advance(TimeSpan.FromMinutes(10));
            (await WhoAmIAsync(treasurer.Access)).Should().Be(HttpStatusCode.OK);
        }

        // Then unused for sixteen minutes: the treasurer's session has timed out, for the access token and for refresh.
        Clock.Advance(TimeSpan.FromMinutes(16));
        (await WhoAmIAsync(treasurer.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(treasurer.Refresh)).Should().Be(HttpStatusCode.BadRequest);
        (await WhoAmIAsync(planner.Access)).Should().Be(HttpStatusCode.OK, "the planner's role has no idle timeout");
        (await RefreshAsync(planner.Refresh)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ending_one_session_stops_its_tokens_and_leaves_the_others()
    {
        await CreateUserAsync("ann");
        var laptop = await SignInAsync("ann", device: "Laptop");
        var phone = await SignInAsync("ann", device: "Phone");

        using (var client = NewClient())
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", laptop.Access);
            (await client.DeleteAsync($"/account/sign-ins/{phone.Sid}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await client.DeleteAsync($"/account/sign-ins/{phone.Sid}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "it already ended");
            (await client.DeleteAsync($"/account/sign-ins/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        (await WhoAmIAsync(phone.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(phone.Refresh)).Should().Be(HttpStatusCode.BadRequest);
        (await WhoAmIAsync(laptop.Access)).Should().Be(HttpStatusCode.OK);
        (await ListAsync(laptop.Access)).Should().ContainSingle().Which.GetProperty("deviceName").GetString().Should().Be("Laptop");
    }

    [Fact]
    public async Task One_person_cannot_end_another_persons_session_and_sign_out_everywhere_ends_them_all()
    {
        await CreateUserAsync("ann");
        await CreateUserAsync("bob");
        var ann = await SignInAsync("ann");
        var annSecond = await SignInAsync("ann");
        var bob = await SignInAsync("bob");

        using (var bobClient = NewClient())
        {
            bobClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bob.Access);
            (await bobClient.DeleteAsync($"/account/sign-ins/{ann.Sid}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        (await WhoAmIAsync(ann.Access)).Should().Be(HttpStatusCode.OK);

        using (var annClient = NewClient())
        {
            annClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ann.Access);
            (await annClient.PostAsync("/account/sessions/revoke-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await WhoAmIAsync(ann.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await WhoAmIAsync(annSecond.Access)).Should().Be(HttpStatusCode.Unauthorized);
        (await WhoAmIAsync(bob.Access)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_refresh_continues_the_same_session()
    {
        await CreateUserAsync("ann");
        var signedIn = await SignInAsync("ann");
        Clock.Advance(TimeSpan.FromMinutes(5));

        var (status, body) = await TokenAsync(("grant_type", "refresh_token"), ("client_id", SpaClient), ("refresh_token", signedIn.Refresh));

        status.Should().Be(HttpStatusCode.OK, body.ToString());
        Jwt(body).GetClaim("sid").Value.Should().Be(signedIn.Sid);
        (await ListAsync(body.GetProperty("access_token").GetString()!)).Should().ContainSingle();
    }
}

/// <summary>The other limit behaviour: refuse the new sign-in instead of ending the oldest session.</summary>
[Trait("Category", "Unit")]
public sealed class SessionRefuseHttpTests : OidcHostTestBase
{
    protected override void ConfigureSettings(Dictionary<string, string?> settings)
    {
        settings["Identity:Sessions:Enabled"] = "true";
        settings["Identity:Sessions:MaxConcurrent"] = "1";
        settings["Identity:Sessions:OnLimit"] = "Refuse";
        settings["Identity:ValidateTokenEntries"] = "false";
    }

    [Fact]
    public async Task A_second_sign_in_is_refused_until_the_first_session_is_ended()
    {
        await CreateUserAsync("ann");
        var (status, first) = await PasswordAsync("ann");
        status.Should().Be(HttpStatusCode.OK, first.ToString());

        var (secondStatus, second) = await PasswordAsync("ann");
        secondStatus.Should().Be(HttpStatusCode.BadRequest);
        second.GetProperty("error").GetString().Should().Be("invalid_grant");
        second.GetProperty("error_description").GetString().Should().Contain("limit is 1");

        using (var client = NewClient())
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", first.GetProperty("access_token").GetString());
            (await client.DeleteAsync($"/account/sign-ins/{Jwt(first).GetClaim("sid").Value}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await PasswordAsync("ann")).Status.Should().Be(HttpStatusCode.OK);
    }
}
