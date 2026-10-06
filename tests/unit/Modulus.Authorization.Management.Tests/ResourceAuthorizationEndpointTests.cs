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
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Xunit;

namespace Modulus.Authorization.Management.Tests;

/// <summary>Per-record questions over HTTP: what may I do to this record, and which rules decided it.</summary>
[Trait("Category", "Unit")]
public sealed class ResourceAuthorizationEndpointTests : IAsyncLifetime
{
    private static readonly Guid Owner = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Other = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private SqliteConnection _connection = null!;
    private WebApplication _app = null!;

    private sealed class Invoice : IHasOwner, IHasWorkflowState
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public string WorkflowState { get; set; } = "";
    }

    private sealed class HttpUser(IHttpContextAccessor http) : ICurrentUser
    {
        private ClaimsPrincipal? Principal => http.HttpContext?.User;
        public Guid? UserId => Guid.TryParse(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
        public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;
        public bool HasPermission(string permission) => Principal?.HasClaim("permission", permission) ?? false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
        : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-User"))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Request.Headers["X-User"].ToString()) };
            claims.AddRange(Request.Headers["X-Permissions"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => new Claim("permission", p)));
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
        builder.Services.AddModulusAuthorization();
        builder.Services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        builder.Services.AddModulusAuthorizationManagement();
        builder.Services.AddResourcePolicy<Invoice>(ResourcePolicy.Define(p => p
            .Allow("edit", r => r.OwnedByCaller() && r.InState("Draft"))
            .Allow("view", _ => true)));
        builder.Services.AddResourceLocator("invoices", (_, id, _) => ValueTask.FromResult<object?>(
            id == "draft"
                ? new Invoice { OwnerId = Owner, WorkflowState = "Draft" }
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

    private HttpClient As(Guid user, string permissions = "")
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-User", user.ToString());
        client.DefaultRequestHeaders.Add("X-Permissions", permissions);
        return client;
    }

    [Fact]
    public async Task Actions_are_what_this_caller_may_do_to_this_record_now()
    {
        var owner = await As(Owner).GetFromJsonAsync<JsonElement>("/authorization/resources/invoices/draft/actions");
        owner.GetProperty("actions").EnumerateArray().Select(a => a.GetString()).Should().Equal("edit", "view");

        var other = await As(Other).GetFromJsonAsync<JsonElement>("/authorization/resources/invoices/draft/actions");
        other.GetProperty("actions").EnumerateArray().Select(a => a.GetString()).Should().Equal("view");
    }

    [Fact]
    public async Task An_unknown_type_or_id_is_a_404_and_anonymous_is_refused()
    {
        (await As(Owner).GetAsync("/authorization/resources/invoices/missing/actions")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await As(Owner).GetAsync("/authorization/resources/unknown/draft/actions")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _app.GetTestClient().GetAsync("/authorization/resources/invoices/draft/actions")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Explain_needs_the_manage_permission_and_shows_the_matched_rules()
    {
        (await As(Owner).GetAsync("/authorization/resources/invoices/draft/explain?action=edit")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        var explained = await As(Other, AuthorizationManagementExtensions.ManagePermission)
            .GetFromJsonAsync<JsonElement>("/authorization/resources/invoices/draft/explain?action=edit");

        explained.GetProperty("allowed").GetBoolean().Should().BeFalse();
        explained.GetProperty("code").GetString().Should().Be("POLICY_VIOLATION");
        explained.GetProperty("rules").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("matched").GetBoolean().Should().BeFalse();
    }
}
