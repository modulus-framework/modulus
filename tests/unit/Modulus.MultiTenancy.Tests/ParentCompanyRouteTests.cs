using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// The parent-to-child routes (decision 6), served in-process with a header-driven test identity. A request with no identity is refused
/// by the route's sign-in requirement (401); a signed-in caller needs the parent permission (a claim here) and a matching authority.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ParentCompanyRouteTests : IAsyncLifetime
{
    private const string Route = "/api/tenancy/tenants";
    private const string AdminUser = "X-Test-User";
    private const string ChildrenClaim = "children";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnection _storeKeepAlive = new("DataSource=:memory:");
    private readonly InMemoryCompanyAuthorityStore _authorities = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private Guid _parent;
    private Guid _child;
    private readonly Guid _manager = Guid.NewGuid();
    private readonly Guid _member = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _storeKeepAlive.OpenAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddMultiTenancy(t => t.UseHeaderResolver());
        builder.Services.AddEfCoreTenantStore(o => o.UseSqlite(_storeKeepAlive));
        builder.Services.AddModulusTenantAdmin();
        builder.Services.AddSingleton<ICompanyAuthorityStore>(_authorities);
        builder.Services.AddAuthentication(TestIdentity.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestIdentity>(TestIdentity.SchemeName, _ => { });
        builder.Services.AddAuthorization(o =>
        {
            // The company administration group is open to any signed-in caller here: these tests exercise the child routes' own checks.
            o.AddPolicy(TenantAdminEndpoints.ManagePermission, p => p.RequireAssertion(_ => true));
            o.AddPolicy(ParentCompanyAuthority.ChildrenManagePermission, p => p.RequireAssertion(ctx => ctx.User.HasClaim(ClaimTypes.Role, ChildrenClaim)));
        });

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapModulusTenantAdmin<TenantEntity, TenantMembershipEntity, TenantDataStoreEntity>();
        await _app.StartAsync();
        _client = ((TestServer)_app.Services.GetRequiredService<IServer>()).CreateClient();

        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>().Database.EnsureCreatedAsync();
        _parent = await CreateAsync("holding");
        _child = await CreateAsync("branch");
        (await _client.PutAsJsonAsync($"{Route}/{_child:D}/parent", new TenantParentRequest(_parent))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _storeKeepAlive.Dispose();
    }

    [Fact]
    public async Task A_caller_with_no_identity_is_refused_before_the_checks_run()
    {
        var response = await _client.PostAsJsonAsync(Members(_parent, _child), new TenantMemberRequest(_member));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_signed_in_caller_without_the_parent_permission_is_refused()
    {
        await GrantAsync(_manager, CompanyOperation.ManageMembers);

        var response = await SignedIn(_manager).PostAsJsonAsync(Members(_parent, _child), new TenantMemberRequest(_member));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("error").GetString().Should().Be("CHILD_AUTHORITY_DENIED");
        (await ActiveMembersAsync(_child)).Should().NotContain(_member);
    }

    [Fact]
    public async Task A_parent_administrator_with_the_permission_and_a_matching_authority_adds_and_removes_a_child_member()
    {
        await GrantAsync(_manager, CompanyOperation.ManageMembers);
        var caller = SignedIn(_manager, ChildrenClaim);

        (await caller.PostAsJsonAsync(Members(_parent, _child), new TenantMemberRequest(_member))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ActiveMembersAsync(_child)).Should().Contain(_member);

        (await caller.DeleteAsync($"{Members(_parent, _child)}/{_member:D}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ActiveMembersAsync(_child)).Should().NotContain(_member);
    }

    [Fact]
    public async Task A_manage_members_authority_does_not_allow_the_baseline_grant_operation()
    {
        await GrantAsync(_manager, CompanyOperation.ManageMembers);

        var response = await SignedIn(_manager, ChildrenClaim).PostAsync($"{Route}/{_parent:D}/children/{_child:D}/baseline", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_revoked_authority_stops_the_next_request()
    {
        await GrantAsync(_manager, CompanyOperation.ManageMembers);
        var caller = SignedIn(_manager, ChildrenClaim);
        (await caller.PostAsJsonAsync(Members(_parent, _child), new TenantMemberRequest(_member))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var grant = (await _authorities.ListAsync(_parent, _child)).Single();
        (await _client.DeleteAsync($"{Route}/{_parent:D}/children/{_child:D}/authority/{grant.Id:D}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await caller.PostAsJsonAsync(Members(_parent, _child), new TenantMemberRequest(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_administrator_cannot_grant_company_authority_to_themselves()
    {
        var response = await SignedIn(_manager).PostAsJsonAsync(
            $"{Route}/{_parent:D}/children/{_child:D}/authority",
            new CompanyAuthorityRequest(_manager, "ManageMembers"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _authorities.ListAsync(_parent, _child)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_authority_is_granted_listed_and_refused_for_a_company_that_is_not_a_child()
    {
        var granted = await SignedIn(_member).PostAsJsonAsync(
            $"{Route}/{_parent:D}/children/{_child:D}/authority",
            new CompanyAuthorityRequest(_manager, "ManageMembers"));
        granted.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await (await _client.GetAsync($"{Route}/{_parent:D}/children/{_child:D}/authority")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        list.EnumerateArray().Should().ContainSingle(g => g.GetProperty("principalId").GetGuid() == _manager && g.GetProperty("isActive").GetBoolean());

        var stranger = await _client.PostAsJsonAsync(
            $"{Route}/{_child:D}/children/{_parent:D}/authority",
            new CompanyAuthorityRequest(_manager, "ManageMembers"));
        stranger.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the parent company is not a child of the company named as its child");
    }

    private static string Members(Guid parent, Guid child) => $"{Route}/{parent:D}/children/{child:D}/members";

    private HttpClient SignedIn(Guid user, string? permission = null)
    {
        var client = ((TestServer)_app.Services.GetRequiredService<IServer>()).CreateClient();
        client.DefaultRequestHeaders.Add(AdminUser, user.ToString("D"));
        if (permission is not null)
            client.DefaultRequestHeaders.Add(TestIdentity.RoleHeader, permission);
        return client;
    }

    private Task GrantAsync(Guid user, CompanyOperation operation)
        => _authorities.GrantAsync(_parent, _child, user, operation, null, DateTimeOffset.UtcNow);

    private async Task<Guid> CreateAsync(string slug)
    {
        var created = await _client.PostAsJsonAsync(Route, new TenantCreateRequest(slug, null, null));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();
    }

    private async Task<List<Guid>> ActiveMembersAsync(Guid company)
    {
        var members = await (await _client.GetAsync($"{Route}/{company:D}/members")).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return members.EnumerateArray().Where(m => m.GetProperty("isActive").GetBoolean()).Select(m => m.GetProperty("userId").GetGuid()).ToList();
    }

    /// <summary>Signs the caller in from the request headers: the user id, and a role claim when the caller holds the children permission.</summary>
    private sealed class TestIdentity(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string RoleHeader = "X-Test-Role";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(AdminUser, out var user) || !Guid.TryParse(user, out var id))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id.ToString("D")) };
            if (Request.Headers.TryGetValue(RoleHeader, out var role) && role == ChildrenClaim)
                claims.Add(new Claim(ClaimTypes.Role, ChildrenClaim));
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
