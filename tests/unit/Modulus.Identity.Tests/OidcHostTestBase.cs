using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Modulus.Authorization;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Modulus.Identity.EntityFrameworkCore.Extensions;
using Modulus.Identity.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Modulus.Identity.Tests;

/// <summary>
/// A full OpenIddict server and validation stack on a TestServer, SQLite and development certificates, with every grant
/// switched on, a few clients and roles seeded, and a bearer-protected endpoint (plus a step-up one). Test classes call the
/// token endpoint over HTTP the way a client does; <see cref="ConfigureSettings"/> changes the server's settings.
/// </summary>
public abstract class OidcHostTestBase : IAsyncLifetime
{
    protected static readonly Guid s_company = Guid.Parse("33333333-3333-3333-3333-333333333333");
    protected const string Password = "Passw0rd!x";
    protected const string SpaClient = "spa";
    protected const string Tablet = "tablet-1";
    protected const string RedirectUri = "https://app.example.test/callback";

    /// <summary>Starts at the real time and moves only when a test advances it (token lifetimes, session timeouts).</summary>
    protected sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    protected ManualClock Clock { get; } = new();

    private sealed class FakeFederatedReader : IFederatedTokenReader
    {
        public Task<System.Security.Claims.ClaimsPrincipal?> ReadAsync(string token, CancellationToken ct = default)
        {
            var (subject, email) = token switch
            {
                "good-token" => ("ext-1", "newbie@example.test"),
                "link-token" => ("ext-2", "ann@example.test"),
                "foreign-token" => ("ext-3", "mallory@evil.test"),
                _ => (null, null),
            };
            if (subject is null)
                return Task.FromResult<System.Security.Claims.ClaimsPrincipal?>(null);
            return Task.FromResult<System.Security.Claims.ClaimsPrincipal?>(new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim("sub", subject),
                new System.Security.Claims.Claim("email", email!),
                new System.Security.Claims.Claim("email_verified", "true"),
                new System.Security.Claims.Claim("name", "New Person"),
            ], "fake")));
        }
    }

    protected SqliteConnection _connection = null!;
    protected WebApplication _app = null!;

    /// <summary>Lets a test class change the server's settings.</summary>
    protected virtual void ConfigureSettings(Dictionary<string, string?> settings)
    {
    }

    /// <summary>Lets a test class register more services.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    /// <summary>Lets a test class map more endpoints.</summary>
    protected virtual void ConfigureEndpoints(WebApplication app)
    {
    }

    /// <summary>The settings the host was built from (after <see cref="ConfigureSettings"/>).</summary>
    protected IConfiguration Configuration { get; private set; } = null!;

    /// <summary>The <c>logout_token</c>s the test application at <c>/test/rp-logout</c> received (back-channel logout).</summary>
    protected List<string> ReceivedLogoutTokens { get; } = [];

    /// <summary>The status the test application answers a logout notification with.</summary>
    protected int RpStatus { get; set; } = 200;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var settings = new Dictionary<string, string?>
        {
            ["Identity:UseDevelopmentCertificates"] = "true",
            ["Identity:AllowPasswordFlow"] = "true",
            ["Identity:EncryptAccessTokens"] = "false",
            ["Identity:ValidateTokenEntries"] = "true",
            ["Identity:AllowAuthorizationCodeFlow"] = "true",
            ["Identity:AllowDeviceCodeFlow"] = "true",
            ["Identity:ShopFloor:Enabled"] = "true",
            ["Identity:FederatedLogin:Enabled"] = "true",
            ["Identity:FederatedLogin:MetadataAddress"] = "https://idp.example.test/.well-known/openid-configuration",
            ["Identity:FederatedLogin:Audiences:0"] = "erp",
            ["Identity:FederatedLogin:LinkByVerifiedEmail"] = "true",
            ["Identity:FederatedLogin:AllowedEmailDomains:0"] = "example.test",
            ["Identity:Passkeys:Enabled"] = "true",
            ["Identity:Passkeys:RelyingPartyId"] = SoftwareAuthenticator.RpId,
            ["Identity:Passkeys:Origins:0"] = SoftwareAuthenticator.Origin,
        };
        ConfigureSettings(settings);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        Configuration = configuration;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(Clock);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
        builder.Services.AddDbContext<ModulusIdentityDbContext>(o => o.UseSqlite(_connection));
        builder.Services.AddModulusOpenIddict(configuration);
        builder.Services.AddModulusIdentity<ModulusIdentityDbContext, ModulusUser, ModulusRole>(configuration);
        builder.Services.AddModulusIdentityStore<ModulusIdentityDbContext>();
        builder.Services.AddSingleton<IFederatedTokenReader, FakeFederatedReader>();
        builder.Services.AddModulusFederatedLogin<ModulusUser, ModulusRole>(configuration);
        builder.Services.AddModulusShopFloor<ModulusUser, ModulusRole>(configuration);
        builder.Services.AddModulusPasskeys<ModulusIdentityDbContext, ModulusUser>(configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(ModulusTokenController).Assembly);
        builder.Services.AddAuthentication().Services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(o =>
            o.DefaultScheme = o.DefaultAuthenticateScheme = o.DefaultChallengeScheme = o.DefaultForbidScheme =
                OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        builder.Services.AddAuthorization();
        builder.Services.AddModulusAuthorization(o => o.StepUp.RequireForCritical = true);
        builder.Services.AddPermissionGrants(grants => grants.GrantToRole("Treasurer", "demo:pay"));
        builder.Services.AddPermissions("Demo", registry => registry.Add("demo:pay", "Release a payment.", null, PermissionSensitivity.Critical));

        ConfigureServices(builder.Services);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        _app.MapModulusPasskeys();
        ConfigureEndpoints(_app);
        // Stands in for the login page: signs the named user in through the Identity cookie.
        _app.MapGet("/test/login/{name}", async (string name, UserManager<ModulusUser> users, SignInManager<ModulusUser> signIn) =>
        {
            await signIn.SignInAsync((await users.FindByNameAsync(name))!, isPersistent: false);
            return Results.Ok();
        }).AllowAnonymous();
        // A relying party's back-channel logout endpoint: records the token and answers with RpStatus.
        _app.MapPost("/test/rp-logout", (HttpContext http) =>
        {
            lock (ReceivedLogoutTokens)
                ReceivedLogoutTokens.Add(http.Request.Form["logout_token"].ToString());
            return Results.StatusCode(RpStatus);
        }).AllowAnonymous();
        _app.MapGet("/whoami", (HttpContext http) => http.User.FindFirst("sub")?.Value).RequireAuthorization();
        _app.MapPost("/pay", () => "paid").RequireAuthorization("demo:pay");

        using (var scope = _app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ModulusIdentityDbContext>().Database.EnsureCreatedAsync();
            await SeedAsync(scope.ServiceProvider);
        }

        await _app.StartAsync();
    }

    protected static async Task SeedAsync(IServiceProvider services)
    {
        var apps = services.GetRequiredService<IOpenIddictApplicationManager>();
        foreach (var clientId in new[] { SpaClient, Tablet })
        {
            var descriptor = new OpenIddictApplicationDescriptor { ClientId = clientId, ClientType = ClientTypes.Public };
            descriptor.Permissions.UnionWith(
            [
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.Password,
                Permissions.GrantTypes.RefreshToken,
                Permissions.Prefixes.GrantType + PasskeyGrant.GrantType,
                Permissions.Prefixes.GrantType + ShopFloorGrant.GrantType,
                Permissions.Prefixes.GrantType + FederatedLoginGrant.GrantType,
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.DeviceAuthorization,
                Permissions.GrantTypes.DeviceCode,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Scope + Scopes.OpenId,
                Permissions.Prefixes.Scope + Scopes.OfflineAccess,
                Permissions.Scopes.Roles,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
            ]);
            descriptor.RedirectUris.Add(new Uri(RedirectUri));
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
            await apps.CreateAsync(descriptor);
        }

        // A client that may only use the password grant.
        var limited = new OpenIddictApplicationDescriptor { ClientId = "limited", ClientType = ClientTypes.Public };
        limited.Permissions.UnionWith([Permissions.Endpoints.Token, Permissions.GrantTypes.Password, Permissions.Prefixes.Scope + Scopes.OpenId]);
        await apps.CreateAsync(limited);

        await services.GetRequiredService<IIntegrationClientDirectory>().BindAsync(Tablet, s_company, null);

        var roles = services.GetRequiredService<RoleManager<ModulusRole>>();
        await roles.CreateAsync(new ModulusRole { Name = "Treasurer" });
        await roles.CreateAsync(new ModulusRole { Name = "ShopFloor" });
        await roles.CreateAsync(new ModulusRole { Name = "Planner" });
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _connection.Dispose();
    }

    protected async Task<ModulusUser> CreateUserAsync(string name, params string[] roles)
    {
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ModulusUser>>();
        var user = new ModulusUser { UserName = name, Email = $"{name}@example.test", EmailConfirmed = true };
        (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        foreach (var role in roles)
            (await users.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        return user;
    }

    // OpenIddict only accepts HTTPS requests; the test server serves them under an https base address.
    protected HttpClient NewClient()
    {
        var client = _app.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    protected static FormUrlEncodedContent Form(params (string Key, string Value)[] fields)
        => new(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));

    protected async Task<(HttpStatusCode Status, JsonElement Body)> TokenAsync(params (string Key, string Value)[] fields)
    {
        using var client = NewClient();
        var response = await client.PostAsync("/connect/token", Form(fields));
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    protected Task<(HttpStatusCode Status, JsonElement Body)> PasswordAsync(string user, string password = Password, string? mfa = null, string client = SpaClient)
        => TokenAsync(
            ("grant_type", "password"), ("client_id", client), ("username", user), ("password", password),
            ("scope", "openid offline_access roles"), ("mfa_code", mfa ?? ""));

    protected static JsonWebToken Jwt(JsonElement body) => new JsonWebTokenHandler().ReadJsonWebToken(body.GetProperty("access_token").GetString());

    protected static string[] Amr(JsonWebToken jwt) => [.. jwt.Claims.Where(c => c.Type == "amr").Select(c => c.Value)];

    protected async Task<HttpResponseMessage> CallAsync(HttpMethod method, string path, string? bearer)
    {
        using var client = NewClient();
        using var request = new HttpRequestMessage(method, path);
        if (bearer is not null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        return await client.SendAsync(request);
    }
}
