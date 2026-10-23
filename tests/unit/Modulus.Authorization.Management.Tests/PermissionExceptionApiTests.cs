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

/// <summary>The permission-exception register: what may be recorded, the review rule, revocation and the findings report.</summary>
[Trait("Category", "Unit")]
public sealed class PermissionExceptionApiTests : IAsyncLifetime
{
    private static readonly Guid Admin = Guid.Parse("00000000-0000-0000-0000-00000000a001");
    private static readonly Guid Reviewer = Guid.Parse("00000000-0000-0000-0000-00000000a002");

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
        _app.MapModulusPermissionExceptions();
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext().Database.EnsureCreatedAsync();
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // Authenticates requests with the caller's id; every caller holds authorization:manage.
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
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    private HttpClient As(Guid user)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", user.ToString());
        return client;
    }

    private static object Valid(string permission = "ops:danger", string holder = "Ops", string holderType = "Role") => new
    {
        permission,
        holderType,
        holder,
        justification = "Quarterly maintenance window needs the operation until the change ticket closes.",
        compensatingControl = "Two-person review of every run.",
        businessOwner = "Operations director",
        validUntil = DateTimeOffset.UtcNow.AddDays(30),
    };

    [Fact]
    public async Task Unauthenticated_requests_are_refused()
    {
        using var anonymous = _app.GetTestClient();
        (await anonymous.GetAsync("/authorization/exceptions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Only_a_critical_permission_can_have_an_exception()
    {
        var response = await As(Admin).PostAsJsonAsync("/authorization/exceptions", Valid(permission: "ops:read"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Critical");
    }

    [Fact]
    public async Task A_justification_is_required_and_the_duration_is_capped()
    {
        var shortReason = new { permission = "ops:danger", holderType = "Role", holder = "Ops", justification = "too short", businessOwner = "Owner", validUntil = DateTimeOffset.UtcNow.AddDays(10) };
        var tooLong = new { permission = "ops:danger", holderType = "Role", holder = "Ops", justification = "A justification long enough to pass the check.", businessOwner = "Owner", validUntil = DateTimeOffset.UtcNow.AddDays(400) };

        (await As(Admin).PostAsJsonAsync("/authorization/exceptions", shortReason)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await As(Admin).PostAsJsonAsync("/authorization/exceptions", tooLong)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_person_cannot_record_an_exception_for_themselves()
    {
        var response = await As(Admin).PostAsJsonAsync("/authorization/exceptions", Valid(holderType: "User", holder: Admin.ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_valid_exception_is_recorded_and_active()
    {
        var response = await As(Admin).PostAsJsonAsync("/authorization/exceptions", Valid());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var view = await response.Content.ReadFromJsonAsync<ExceptionView>();
        view!.Active.Should().BeTrue();
        view.RequestedBy.Should().Be(Admin);
        view.ReviewedAt.Should().BeNull();
    }

    [Fact]
    public async Task The_requester_cannot_review_and_another_person_can_review_once()
    {
        var id = (await (await As(Admin).PostAsJsonAsync("/authorization/exceptions", Valid())).Content.ReadFromJsonAsync<ExceptionView>())!.Id;

        (await As(Admin).PostAsync($"/authorization/exceptions/{id}/review", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await As(Reviewer).PostAsync($"/authorization/exceptions/{id}/review", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await As(Reviewer).PostAsync($"/authorization/exceptions/{id}/review", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_revoked_exception_is_no_longer_active_and_cannot_be_revoked_twice()
    {
        var id = (await (await As(Admin).PostAsJsonAsync("/authorization/exceptions", Valid())).Content.ReadFromJsonAsync<ExceptionView>())!.Id;

        (await As(Reviewer).PostAsync($"/authorization/exceptions/{id}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await As(Reviewer).PostAsync($"/authorization/exceptions/{id}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var active = await As(Admin).GetFromJsonAsync<ExceptionView[]>("/authorization/exceptions?active=true");
        active.Should().NotContain(e => e.Id == id);
    }

    [Fact]
    public async Task The_findings_list_a_privileged_grant_until_an_exception_explains_it()
    {
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<EfPermissionGrantStore>().GrantToRoleAsync("Ops", ["ops:danger"]);

        var before = await As(Admin).GetFromJsonAsync<FindingView[]>("/authorization/exceptions/findings");
        before.Should().ContainSingle(f => f.Permission == "ops:danger" && f.Holder == "Ops" && !f.HadExpiredException);

        await As(Admin).PostAsJsonAsync("/authorization/exceptions", Valid());

        var after = await As(Admin).GetFromJsonAsync<FindingView[]>("/authorization/exceptions/findings");
        after.Should().NotContain(f => f.Permission == "ops:danger" && f.Holder == "Ops");
    }

    private sealed record ExceptionView(Guid Id, bool Active, Guid RequestedBy, DateTimeOffset? ReviewedAt);

    private sealed record FindingView(string Permission, string HolderType, string Holder, bool HadExpiredException);
}
