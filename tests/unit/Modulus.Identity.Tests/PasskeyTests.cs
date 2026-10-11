using System.Formats.Cbor;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>Passkeys end to end against the real Fido2 verifier, with a software authenticator that signs like a browser would.</summary>
[Trait("Category", "Unit")]
public sealed class PasskeyTests : IAsyncLifetime
{
    private const string Origin = SoftwareAuthenticator.Origin;
    private const string RpId = SoftwareAuthenticator.RpId;

    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;
    private readonly ManualClock _clock = new();

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Acme ERP";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-UserId", out var userId))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
            if (Request.Headers.TryGetValue("X-Test-AuthTime", out var authTime))
                claims.Add(new Claim("auth_time", authTime.ToString(), ClaimValueTypes.Integer64));
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:Passkeys:Enabled"] = "true",
            ["Identity:Passkeys:RelyingPartyId"] = RpId,
            ["Identity:Passkeys:Origins:0"] = Origin,
            ["Identity:Passkeys:MaxPasskeysPerUser"] = "2",
        }).Build();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new FakeEnvironment());
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
        builder.Services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(_connection));
        builder.Services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(configuration);
        builder.Services.AddModulusPasskeys<ModulusIdentityDbContext, ModulusUser>(configuration);
        builder.Services.PostConfigure<AuthenticationOptions>(o =>
            o.DefaultScheme = o.DefaultAuthenticateScheme = o.DefaultChallengeScheme = o.DefaultForbidScheme = "Test");

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapModulusPasskeys();

        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _connection.Dispose();
    }

    private async Task<ModulusUser> CreateUserAsync(string name = "ann", bool active = true)
    {
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var user = new ModulusUser { UserName = name, Email = $"{name}@example.test", IsActive = active, EmailConfirmed = true };
        (await users.CreateAsync(user, "Passw0rd!x")).Succeeded.Should().BeTrue();
        return user;
    }

    private HttpClient Client(Guid? user = null, TimeSpan? signedInAgo = null)
    {
        var client = _app.GetTestClient();
        if (user is { } id)
            client.DefaultRequestHeaders.Add("X-Test-UserId", id.ToString());
        if (signedInAgo is { } ago)
            client.DefaultRequestHeaders.Add("X-Test-AuthTime", (_clock.GetUtcNow() - ago).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        return client;
    }

    private async Task<PasskeyInfo> RegisterAsync(ModulusUser user, SoftwareAuthenticator device, string name = "Work laptop")
    {
        using var client = Client(user.Id, TimeSpan.FromMinutes(1));
        var begin = await client.PostAsync("/account/passkeys/register/options", null);
        begin.StatusCode.Should().Be(HttpStatusCode.OK, await begin.Content.ReadAsStringAsync());
        var ceremony = await begin.Content.ReadFromJsonAsync<JsonElement>();
        var complete = await client.PostAsJsonAsync("/account/passkeys/register/complete", new
        {
            challengeId = ceremony.GetProperty("challengeId").GetGuid(),
            name,
            credential = JsonDocument.Parse(device.Register(ceremony.GetProperty("options"))).RootElement,
        });
        complete.StatusCode.Should().Be(HttpStatusCode.OK, await complete.Content.ReadAsStringAsync());
        return (await complete.Content.ReadFromJsonAsync<PasskeyInfo>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    private async Task<(Guid ChallengeId, JsonElement Options)> BeginLoginAsync()
    {
        using var client = Client();
        var response = await client.PostAsync("/account/passkeys/login/options", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ceremony = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (ceremony.GetProperty("challengeId").GetGuid(), ceremony.GetProperty("options"));
    }

    private async Task<PasswordGrantResult> LoginAsync(Guid challengeId, string assertion)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPasskeyService>().ValidateLoginAsync(challengeId, assertion);
    }

    [Fact]
    public async Task A_registered_passkey_signs_the_person_in_with_multi_factor_methods()
    {
        var user = await CreateUserAsync();
        var device = new SoftwareAuthenticator();
        var created = await RegisterAsync(user, device);
        created.Name.Should().Be("Work laptop");

        var (challengeId, options) = await BeginLoginAsync();
        options.GetProperty("rpId").GetString().Should().Be(RpId);
        options.GetProperty("userVerification").GetString().Should().Be("required");

        var result = await LoginAsync(challengeId, device.Assert(options, user.Id));
        result.Success.Should().BeTrue();
        result.Subject.Should().Be(user.Id.ToString());
        result.UserName.Should().Be("ann");
        result.AuthenticationMethods.Should().BeEquivalentTo("hwk", "user", "mfa");

        using var client = Client(user.Id);
        var list = await client.GetFromJsonAsync<JsonElement[]>("/account/passkeys");
        list.Should().ContainSingle().Which.GetProperty("lastUsedAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task A_ceremony_can_be_used_once_and_only_before_it_expires()
    {
        var user = await CreateUserAsync();
        var device = new SoftwareAuthenticator();
        await RegisterAsync(user, device);

        var (challengeId, options) = await BeginLoginAsync();
        var assertion = device.Assert(options, user.Id);
        (await LoginAsync(challengeId, assertion)).Success.Should().BeTrue();
        (await LoginAsync(challengeId, assertion)).Success.Should().BeFalse("a signed answer cannot be replayed");

        var (lateId, lateOptions) = await BeginLoginAsync();
        _clock.Advance(TimeSpan.FromMinutes(6));
        (await LoginAsync(lateId, device.Assert(lateOptions, user.Id))).Success.Should().BeFalse("the ceremony timed out");

        (await LoginAsync(Guid.NewGuid(), assertion)).Success.Should().BeFalse();
    }

    [Fact]
    public async Task A_wrong_origin_a_missing_user_verification_or_a_cloned_counter_is_refused()
    {
        var user = await CreateUserAsync();
        var device = new SoftwareAuthenticator();
        await RegisterAsync(user, device);

        var (a, optionsA) = await BeginLoginAsync();
        (await LoginAsync(a, device.Assert(optionsA, user.Id, origin: "https://evil.example.com"))).Success.Should().BeFalse("a phishing site's origin");

        var (b, optionsB) = await BeginLoginAsync();
        (await LoginAsync(b, device.Assert(optionsB, user.Id, flags: 0x01))).Success.Should().BeFalse("user verification is required");

        var (c, optionsC) = await BeginLoginAsync();
        (await LoginAsync(c, device.Assert(optionsC, user.Id, counter: 10))).Success.Should().BeTrue();
        var (d, optionsD) = await BeginLoginAsync();
        (await LoginAsync(d, device.Assert(optionsD, user.Id, counter: 5))).Success.Should().BeFalse("a counter that goes back means a cloned authenticator");
    }

    [Fact]
    public async Task A_disabled_or_unknown_account_and_an_unknown_credential_are_refused()
    {
        var user = await CreateUserAsync();
        var device = new SoftwareAuthenticator();
        await RegisterAsync(user, device);

        var stranger = new SoftwareAuthenticator();
        var (a, optionsA) = await BeginLoginAsync();
        (await LoginAsync(a, stranger.Assert(optionsA, user.Id))).Success.Should().BeFalse("nobody registered this credential");

        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
            var stored = await users.FindByIdAsync(user.Id.ToString());
            stored!.IsActive = false;
            await users.UpdateAsync(stored);
        }

        var (b, optionsB) = await BeginLoginAsync();
        (await LoginAsync(b, device.Assert(optionsB, user.Id))).Success.Should().BeFalse("the account is disabled");
    }

    [Fact]
    public async Task Managing_passkeys_needs_a_recent_sign_in_a_limit_and_only_your_own()
    {
        var ann = await CreateUserAsync("ann");
        var bob = await CreateUserAsync("bob");

        // No auth_time (an old token) or an old sign-in: refused before anything is created.
        using (var old = Client(ann.Id))
            (await old.PostAsync("/account/passkeys/register/options", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using (var stale = Client(ann.Id, TimeSpan.FromMinutes(30)))
            (await stale.PostAsync("/account/passkeys/register/options", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using (var anonymous = Client())
            (await anonymous.PostAsync("/account/passkeys/register/options", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var first = await RegisterAsync(ann, new SoftwareAuthenticator(), "one");
        await RegisterAsync(ann, new SoftwareAuthenticator(), "two");
        using (var client = Client(ann.Id, TimeSpan.FromMinutes(1)))
            (await client.PostAsync("/account/passkeys/register/options", null)).StatusCode.Should().Be(HttpStatusCode.Conflict, "the limit is 2");

        // Bob cannot remove or even see Ann's.
        using (var bobClient = Client(bob.Id, TimeSpan.FromMinutes(1)))
        {
            (await bobClient.DeleteAsync($"/account/passkeys/{first.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await bobClient.GetFromJsonAsync<JsonElement[]>("/account/passkeys")).Should().BeEmpty();
        }

        using var annClient = Client(ann.Id, TimeSpan.FromMinutes(1));
        (await annClient.DeleteAsync($"/account/passkeys/{first.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await annClient.GetFromJsonAsync<JsonElement[]>("/account/passkeys")).Should().ContainSingle();
    }

    [Fact]
    public async Task A_registration_for_one_account_cannot_be_finished_by_another_and_a_bad_attestation_is_refused()
    {
        var ann = await CreateUserAsync("ann");
        var bob = await CreateUserAsync("bob");
        var device = new SoftwareAuthenticator();

        using var annClient = Client(ann.Id, TimeSpan.FromMinutes(1));
        var ceremony = await (await annClient.PostAsync("/account/passkeys/register/options", null)).Content.ReadFromJsonAsync<JsonElement>();
        var credential = JsonDocument.Parse(device.Register(ceremony.GetProperty("options"))).RootElement;

        using var bobClient = Client(bob.Id, TimeSpan.FromMinutes(1));
        var stolen = await bobClient.PostAsJsonAsync("/account/passkeys/register/complete",
            new { challengeId = ceremony.GetProperty("challengeId").GetGuid(), name = "x", credential });
        stolen.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The ceremony was spent by the failed attempt: even Ann cannot reuse it.
        var again = await annClient.PostAsJsonAsync("/account/passkeys/register/complete",
            new { challengeId = ceremony.GetProperty("challengeId").GetGuid(), name = "x", credential });
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // An attestation made for another origin is refused.
        var second = await (await annClient.PostAsync("/account/passkeys/register/options", null)).Content.ReadFromJsonAsync<JsonElement>();
        var phished = await annClient.PostAsJsonAsync("/account/passkeys/register/complete", new
        {
            challengeId = second.GetProperty("challengeId").GetGuid(),
            name = "x",
            credential = JsonDocument.Parse(device.Register(second.GetProperty("options"), origin: "https://evil.example.com")).RootElement,
        });
        phished.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await annClient.GetFromJsonAsync<JsonElement[]>("/account/passkeys")).Should().BeEmpty();
    }
}
