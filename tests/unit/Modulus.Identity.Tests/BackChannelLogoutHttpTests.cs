using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using OpenIddict.Server;
using Xunit;
using Microsoft.EntityFrameworkCore;

namespace Modulus.Identity.Tests;

/// <summary>OpenID Connect Back-Channel Logout: an ended session is reported to its application with a signed logout token, queued and retried.</summary>
[Trait("Category", "Unit")]
public sealed class BackChannelLogoutHttpTests : OidcHostTestBase
{
    private const string RpAddress = "https://localhost/test/rp-logout";

    protected override void ConfigureSettings(Dictionary<string, string?> settings)
    {
        settings["Identity:Issuer"] = "https://localhost/";
        settings["Identity:Sessions:Enabled"] = "true";
        settings["Identity:Sessions:MaxConcurrent"] = "2";
        settings["Identity:Sessions:BackChannelLogout:Enabled"] = "true";
        settings["Identity:Sessions:BackChannelLogout:MaxAttempts"] = "3";
        settings["Identity:Sessions:BackChannelLogout:RetryBackoffSeconds"] = "30";
        // The hosted service ticks every hour here; the tests drive delivery themselves.
        settings["Identity:Sessions:BackChannelLogout:PollingSeconds"] = "3600";
        // The test application is registered at localhost, which is a private address unless this is allowed (development only).
        settings["Identity:Sessions:BackChannelLogout:AllowPrivateNetworks"] = "true";
        settings["Identity:AccessTokenLifetimeMin"] = "1440";
        settings["Identity:ValidateTokenEntries"] = "false";
    }

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddHttpClient("modulus-backchannel-logout")
            .ConfigurePrimaryHttpMessageHandler(sp => ((TestServer)sp.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()).CreateHandler());

    private async Task RegisterAsync(string client = SpaClient, string address = RpAddress)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IBackChannelLogoutDirectory>().SetAsync(client, new Uri(address))).Should().BeTrue();
    }

    private async Task<(string Access, string Sid, JsonElement Body)> SignInAsync(string user)
    {
        var (status, body) = await PasswordAsync(user);
        status.Should().Be(HttpStatusCode.OK, body.ToString());
        return (body.GetProperty("access_token").GetString()!, Jwt(body).GetClaim("sid").Value, body);
    }

    private async Task EndAsync(string bearer, string sid)
    {
        using var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        (await client.DeleteAsync($"/account/sign-ins/{sid}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<int> DeliverAsync()
    {
        var service = _app.Services.GetServices<IHostedService>().OfType<BackChannelLogoutService<ModulusIdentityDbContext>>().Single();
        return await service.DeliverDueAsync(CancellationToken.None);
    }

    private async Task<List<ModulusLogoutNotification>> QueueAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().LogoutNotifications.AsNoTracking().ToListAsync();
    }

    private async Task<TokenValidationResult> ValidateAsync(string token)
    {
        var key = _app.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue.SigningCredentials.First().Key;
        return await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "https://localhost/",
            ValidAudience = SpaClient,
            IssuerSigningKey = key,
            ValidTypes = ["logout+jwt"],
        });
    }

    [Fact]
    public async Task Ending_a_session_sends_the_application_a_signed_logout_token_matching_its_id_token()
    {
        await RegisterAsync();
        var user = await CreateUserAsync("ann");
        var (access, sid, body) = await SignInAsync("ann");
        new JsonWebTokenHandler().ReadJsonWebToken(body.GetProperty("id_token").GetString()).GetClaim("sid").Value
            .Should().Be(sid, "the application matches the logout token's sid to its id token");

        await EndAsync(access, sid);
        (await DeliverAsync()).Should().Be(1);

        var token = ReceivedLogoutTokens.Should().ContainSingle().Subject;
        var result = await ValidateAsync(token);
        result.IsValid.Should().BeTrue(result.Exception?.Message);
        var jwt = (JsonWebToken)result.SecurityToken;
        jwt.Typ.Should().Be("logout+jwt");
        jwt.Issuer.Should().Be("https://localhost/");
        jwt.Audiences.Should().ContainSingle().Which.Should().Be(SpaClient);
        jwt.GetClaim("sub").Value.Should().Be(user.Id.ToString());
        jwt.GetClaim("sid").Value.Should().Be(sid);
        jwt.GetClaim("jti").Value.Should().NotBeNullOrEmpty();
        jwt.TryGetClaim("nonce", out _).Should().BeFalse("a logout token never has a nonce");
        jwt.GetClaim("events").Value.Should().Contain("http://schemas.openid.net/event/backchannel-logout");
        (jwt.ValidTo - jwt.ValidFrom).TotalSeconds.Should().BeApproximately(120, 2);

        var row = (await QueueAsync()).Should().ContainSingle().Subject;
        row.DeliveredAt.Should().NotBeNull();
        (await DeliverAsync()).Should().Be(0, "a delivered notification is not sent again");
    }

    [Fact]
    public async Task A_failing_application_is_retried_with_backoff_and_then_given_up_on()
    {
        await RegisterAsync();
        await CreateUserAsync("ann");
        var (access, sid, _) = await SignInAsync("ann");
        RpStatus = 500;
        await EndAsync(access, sid);

        (await DeliverAsync()).Should().Be(1);
        var first = (await QueueAsync()).Single();
        first.Attempts.Should().Be(1);
        first.DeliveredAt.Should().BeNull();
        first.LastError.Should().Be("HTTP 500");
        (await DeliverAsync()).Should().Be(0, "the retry waits for its backoff");

        Clock.Advance(TimeSpan.FromSeconds(31));
        (await DeliverAsync()).Should().Be(1);
        (await QueueAsync()).Single().NextAttemptAt.Should().BeAfter(first.NextAttemptAt.AddSeconds(30), "the wait doubles");

        Clock.Advance(TimeSpan.FromMinutes(2));
        (await DeliverAsync()).Should().Be(1);
        var abandoned = (await QueueAsync()).Single();
        abandoned.Attempts.Should().Be(3);
        abandoned.FailedAt.Should().NotBeNull("MaxAttempts is 3");
        Clock.Advance(TimeSpan.FromHours(1));
        (await DeliverAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_recovered_application_gets_the_notification_on_a_later_attempt()
    {
        await RegisterAsync();
        await CreateUserAsync("ann");
        var (access, sid, _) = await SignInAsync("ann");
        RpStatus = 503;
        await EndAsync(access, sid);
        await DeliverAsync();

        RpStatus = 200;
        Clock.Advance(TimeSpan.FromSeconds(31));
        (await DeliverAsync()).Should().Be(1);

        (await QueueAsync()).Single().DeliveredAt.Should().NotBeNull();
        ReceivedLogoutTokens.Should().HaveCount(2, "one failed delivery and one that worked");
    }

    [Fact]
    public async Task Sign_out_everywhere_the_limit_and_the_idle_timeout_all_notify_but_an_unregistered_application_does_not()
    {
        await CreateUserAsync("ann");
        var first = await SignInAsync("ann");
        (await QueueAsync()).Should().BeEmpty("no address is registered yet");
        await EndAsync(first.Access, first.Sid);
        (await QueueAsync()).Should().BeEmpty("an application without a logout address is not notified");

        await RegisterAsync();
        var a = await SignInAsync("ann");
        Clock.Advance(TimeSpan.FromMinutes(1));
        var b = await SignInAsync("ann");
        Clock.Advance(TimeSpan.FromMinutes(1));

        // The limit is 2: a third sign-in ends the least recently used session.
        var c = await SignInAsync("ann");
        (await QueueAsync()).Should().ContainSingle().Which.SessionId.ToString().Should().Be(a.Sid);

        using (var client = NewClient())
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", c.Access);
            (await client.PostAsync("/account/sessions/revoke-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await QueueAsync()).Select(n => n.SessionId.ToString()).Should().BeEquivalentTo(a.Sid, b.Sid, c.Sid);
        (await DeliverAsync()).Should().Be(3);
        ReceivedLogoutTokens.Select(t => new JsonWebTokenHandler().ReadJsonWebToken(t).GetClaim("sid").Value).Should().BeEquivalentTo(a.Sid, b.Sid, c.Sid);
    }

    [Theory]
    [InlineData("https://localhost/logout")]
    [InlineData("https://127.0.0.1/logout")]
    [InlineData("https://10.1.2.3/logout")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::1]/logout")]
    public void Logout_addresses_on_this_machine_or_a_private_network_are_refused_by_default(string address)
        => FluentActions.Invoking(() => OpenIddictBackChannelLogoutDirectory.Validate(new Uri(address), allowInsecure: false, allowPrivateNetworks: false))
            .Should().Throw<ArgumentException>();

    [Fact]
    public async Task Only_safe_logout_addresses_are_accepted()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IBackChannelLogoutDirectory>();

        foreach (var unsafeAddress in new[] { "http://rp.example.test/logout", "https://user:pw@rp.example.test/logout", "https://rp.example.test/logout#frag" })
        {
            var attempt = async () => await directory.SetAsync(SpaClient, new Uri(unsafeAddress));
            await attempt.Should().ThrowAsync<ArgumentException>(unsafeAddress);
        }

        (await directory.SetAsync(SpaClient, new Uri("http://localhost:5000/logout"))).Should().BeTrue("loopback http is allowed for development");
        (await directory.SetAsync("nobody", new Uri(RpAddress))).Should().BeFalse();
        (await directory.FindAsync(SpaClient))!.AbsoluteUri.Should().Be("http://localhost:5000/logout");
        (await directory.SetAsync(SpaClient, null)).Should().BeTrue();
        (await directory.FindAsync(SpaClient)).Should().BeNull();
    }

    [Fact]
    public void Back_channel_logout_without_an_issuer_fails_at_startup()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:Sessions:Enabled"] = "true",
            ["Identity:Sessions:BackChannelLogout:Enabled"] = "true",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();

        var register = () => services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(configuration);

        register.Should().Throw<InvalidOperationException>().WithMessage("*Identity:Issuer*");
    }
}
