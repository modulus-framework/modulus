using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Management;
using Modulus.Authorization.Resources;
using Modulus.Authorization.Sharing;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Xunit;

namespace Modulus.Authorization.Management.Tests;

/// <summary>A8-1 over HTTP: a person who can read a record shares it; the grantee reads it through the share only; the sharer revokes it.</summary>
[Trait("Category", "Unit")]
public sealed class RecordShareEndpointTests : IAsyncLifetime
{
    private static readonly Guid Owner = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Grantee = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Stranger = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid ContractId = Guid.Parse("dddddddd-0000-0000-0000-000000000004");

    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;

    private sealed class Contract : IHasOwner
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
    }

    private sealed class HttpUser(IHttpContextAccessor http) : ICurrentUser
    {
        private ClaimsPrincipal? Principal => http.HttpContext?.User;
        public Guid? UserId => Guid.TryParse(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
        : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-User"))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Request.Headers["X-User"].ToString()) };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test")));
        }
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", null);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpUser>();
        builder.Services.AddScoped<ICurrentDataScope, Modulus.Core.Null.NullCurrentDataScope>();
        builder.Services.AddScoped<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
        builder.Services.AddModulusAuthorization(o => o.TrustPermissionClaims = false);
        builder.Services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        builder.Services.AddModulusAuthorizationManagement();
        builder.Services.AddRecordSharing();
        builder.Services.AddResourcePolicy<Contract>(ResourcePolicy.Define(p => p
            .Allow("view", r => r.OwnedByCaller())));
        builder.Services.AddResourceLocator("contracts", (_, id, _) => ValueTask.FromResult<object?>(
            Guid.TryParse(id, out var parsed) && parsed == ContractId
                ? new Contract { Id = ContractId, OwnerId = Owner }
                : null));

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapModulusResourceAuthorization();
        using (var db = _app.Services.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext())
            db.Database.EnsureCreated();
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        _connection.Dispose();
    }

    private HttpClient As(Guid user)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-User", user.ToString());
        return client;
    }

    private static string Path(string suffix = "") => $"/authorization/resources/contracts/{ContractId}{suffix}";

    private async Task<HttpResponseMessage> ShareAsync(Guid by, Guid to, double hours = 2)
        => await As(by).PostAsJsonAsync(Path("/shares"), new { granteeUserId = to, hours });

    private async Task<IReadOnlyList<string>> ActionsAsync(Guid user)
    {
        var body = await As(user).GetFromJsonAsync<JsonElement>(Path("/actions"));
        return body.GetProperty("actions").EnumerateArray().Select(a => a.GetString()!).ToList();
    }

    [Fact]
    public async Task A_share_gives_the_grantee_the_read_through_the_share_alone()
    {
        (await ActionsAsync(Grantee)).Should().BeEmpty("the grantee cannot read the contract before a share");

        var created = await ShareAsync(Owner, Grantee);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var share = await created.Content.ReadFromJsonAsync<JsonElement>();
        share.GetProperty("active").GetBoolean().Should().BeTrue();
        (await ActionsAsync(Grantee)).Should().Equal("view");
    }

    [Fact]
    public async Task Someone_who_cannot_read_the_record_cannot_share_it()
    {
        (await ShareAsync(Stranger, Grantee)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_grantee_cannot_pass_the_share_on()
    {
        await ShareAsync(Owner, Grantee);

        (await ShareAsync(Grantee, Stranger)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await As(Grantee).GetAsync(Path("/shares"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_owner_lists_the_shares_and_revokes_one_ending_it_at_once()
    {
        var share = await (await ShareAsync(Owner, Grantee)).Content.ReadFromJsonAsync<JsonElement>();
        var id = share.GetProperty("id").GetGuid();

        var listed = await As(Owner).GetFromJsonAsync<JsonElement>(Path("/shares"));
        listed.EnumerateArray().Should().ContainSingle().Which.GetProperty("id").GetGuid().Should().Be(id);

        (await As(Grantee).DeleteAsync($"/authorization/resources/shares/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the grantee cannot end their own share");

        (await As(Owner).DeleteAsync($"/authorization/resources/shares/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ActionsAsync(Grantee)).Should().BeEmpty("the revoked share no longer reads the contract");
        (await As(Owner).DeleteAsync($"/authorization/resources/shares/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Bad_input_is_refused_and_anonymous_is_unauthorized()
    {
        (await ShareAsync(Owner, Grantee, hours: 0)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ShareAsync(Owner, Grantee, hours: 24 * 4000)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ShareAsync(Owner, Owner)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a record cannot be shared with its sharer");
        (await As(Owner).PostAsJsonAsync("/authorization/resources/contracts/missing/shares", new { granteeUserId = Grantee, hours = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _app.GetTestClient().PostAsJsonAsync(Path("/shares"), new { granteeUserId = Grantee, hours = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
