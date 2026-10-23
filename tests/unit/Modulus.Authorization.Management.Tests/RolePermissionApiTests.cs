using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Management;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.Management.Tests;

/// <summary>
/// Role-permission administration (premodule 1, decision D-P1): a role's allow grants are read and set through the same rules as the grant
/// endpoint. An administrator grants only what they hold, a Critical permission needs grant-any, and a role they hold cannot be changed by them.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RolePermissionApiTests : IAsyncLifetime
{
    private static readonly Guid Admin = Guid.Parse("00000000-0000-0000-0000-00000000b001");

    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, CallerHandler>("Test", null);
        builder.Services.AddModulusAuthorization(o => o.TrustPermissionClaims = true);
        builder.Services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        builder.Services.AddModulusAuthorizationManagement();
        builder.Services.AddPermissions("Ops", registry =>
        {
            registry.Add("ops:danger", "A privileged operation.", null, PermissionSensitivity.Critical);
            registry.Add("ops:read", "Read operations.");
        });

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapModulusRolePermissions();
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext().Database.EnsureCreatedAsync();
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    /// <summary>Authenticates with the caller's id and the permissions named in X-Test-Permissions (always authorization:manage).</summary>
    private sealed class CallerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-UserId"))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, Request.Headers["X-Test-UserId"].ToString()),
                new("permission", AuthorizationManagementExtensions.ManagePermission),
            };
            if (Request.Headers.TryGetValue("X-Test-Permissions", out var held))
                claims.AddRange(held.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => new Claim("permission", p)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    private HttpClient As(string[] held)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", Admin.ToString());
        if (held.Length > 0)
            client.DefaultRequestHeaders.Add("X-Test-Permissions", string.Join(',', held));
        return client;
    }

    [Fact]
    public async Task Unauthenticated_requests_are_refused()
    {
        using var anonymous = _app.GetTestClient();
        (await anonymous.GetAsync("/api/identity/roles/Ops/permissions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_administrator_cannot_grant_a_permission_they_do_not_hold()
    {
        var response = await As([]).PutAsJsonAsync("/api/identity/roles/Ops/permissions", new { permissions = new[] { "ops:read" } });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await As([]).GetFromJsonAsync<List<Grant>>("/api/identity/roles/Ops/permissions")).Should().BeEmpty("nothing was granted");
    }

    [Fact]
    public async Task A_grant_any_holder_sets_a_roles_permissions_and_revokes_them()
    {
        var granter = As(["authorization:grant-any", "ops:read", "ops:danger"]);

        (await granter.PutAsJsonAsync("/api/identity/roles/Ops/permissions", new { permissions = new[] { "ops:read", "ops:danger" } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var held = await granter.GetFromJsonAsync<List<Grant>>("/api/identity/roles/Ops/permissions");
        held!.Select(g => g.Permission).Should().BeEquivalentTo(["ops:read", "ops:danger"]);

        var revoked = await granter.PutAsJsonAsync("/api/identity/roles/Ops/permissions", new { permissions = Array.Empty<string>() });
        revoked.StatusCode.Should().Be(HttpStatusCode.OK);
        (await As([]).GetFromJsonAsync<List<Grant>>("/api/identity/roles/Ops/permissions")).Should().BeEmpty();
    }

    [Fact]
    public async Task Without_grant_any_a_critical_permission_is_refused_even_when_the_administrator_holds_it()
    {
        var holder = As(["ops:read", "ops:danger"]);

        var response = await holder.PutAsJsonAsync("/api/identity/roles/Ops/permissions", new { permissions = new[] { "ops:danger" } });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Critical permissions are granted only by grant-any holders");
    }

    [Fact]
    public async Task An_unknown_permission_is_refused()
    {
        var response = await As(["authorization:grant-any"]).PutAsJsonAsync("/api/identity/roles/Ops/permissions", new { permissions = new[] { "ops:invented" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "administrators grant declared permissions, never invented ones");
    }

    private sealed record Grant(string Permission, string Type);
}
