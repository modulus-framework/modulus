using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.MultiTenancy;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// The company switcher's list (decision 29): only the caller's active memberships, closed companies left out, the company in the request
/// marked, and nothing for a caller with no user id.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CompanySwitcherTests : IAsyncLifetime
{
    private static readonly Guid Ada = Guid.Parse("00000000-0000-0000-0000-00000000a001");
    private static readonly Guid Acme = Guid.Parse("00000000-0000-0000-0000-00000000c001");
    private static readonly Guid Beta = Guid.Parse("00000000-0000-0000-0000-00000000c002");
    private static readonly Guid Closed = Guid.Parse("00000000-0000-0000-0000-00000000c003");
    private static readonly Guid Stranger = Guid.Parse("00000000-0000-0000-0000-00000000c004");

    private readonly InMemoryTenantMembershipStore _memberships = new();
    private readonly FakeCompanies _companies = new();
    private Guid? _signedInAs = Ada;
    private Guid? _requestCompany;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _companies.Add(new TenantInfo(Acme, "acme", "Acme Ltd"));
        _companies.Add(new TenantInfo(Beta, "beta", null, Status: TenantStatus.Suspended));
        _companies.Add(new TenantInfo(Closed, "old", null, Status: TenantStatus.Closed));
        _companies.Add(new TenantInfo(Stranger, "other", null));
        _memberships.Add(Ada, Acme);
        _memberships.Add(Ada, Beta);
        _memberships.Add(Ada, Closed);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ITenantMembershipStore>(_memberships);
        builder.Services.AddSingleton<ITenantStore>(_companies);
        builder.Services.AddSingleton<ICurrentTenant, CurrentTenant>();
        builder.Services.AddSingleton<ICurrentUser>(new FixedUser(() => _signedInAs));
        builder.Services.AddAuthentication(StubScheme.Name)
            .AddScheme<AuthenticationSchemeOptions, StubScheme>(StubScheme.Name, _ => { });
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.Use(async (ctx, next) =>
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "caller")], "test"));
            var tenant = ctx.RequestServices.GetRequiredService<ICurrentTenant>();
            if (_requestCompany is { } company && _companies.Find(company) is { } info)
            {
                using (((CurrentTenant)tenant).Change(info))
                    await next(ctx);
            }
            else
            {
                await next(ctx);
            }
        });
        _app.MapModulusCompanySwitcher();
        await _app.StartAsync();
        _client = ((TestServer)_app.Services.GetRequiredService<IServer>()).CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task The_list_holds_only_the_active_memberships_ordered_by_slug()
    {
        var list = await _client.GetFromJsonAsync<List<CompanyChoice>>("/api/account/companies");

        list!.Select(c => c.TenantSlug).Should().Equal(["acme", "beta"], because: "the closed company is left out");
        list![0].DisplayName.Should().Be("Acme Ltd");
        list[1].Status.Should().Be(nameof(TenantStatus.Suspended), "a suspended company is still listed, read-only");
    }

    [Fact]
    public async Task A_company_that_is_not_a_membership_is_never_listed()
    {
        _memberships.Remove(Ada, Beta);

        var list = await _client.GetFromJsonAsync<List<CompanyChoice>>("/api/account/companies");

        list!.Select(c => c.TenantId).Should().NotContain(Stranger);
        list!.Select(c => c.TenantSlug).Should().Equal("acme");
    }

    [Fact]
    public async Task The_company_in_the_request_is_marked_as_current()
    {
        _requestCompany = Acme;

        var list = await _client.GetFromJsonAsync<List<CompanyChoice>>("/api/account/companies");

        list!.Single(c => c.TenantId == Acme).Current.Should().BeTrue();
        list!.Single(c => c.TenantId == Beta).Current.Should().BeFalse();
    }

    [Fact]
    public async Task A_caller_with_no_user_id_gets_no_list()
    {
        _signedInAs = null;

        var response = await _client.GetAsync("/api/account/companies");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class StubScheme(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggers,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggers, encoder)
    {
        public const string Name = "Stub";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "caller")], Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
        }
    }

    private sealed record FixedUser(Func<Guid?> Id) : ICurrentUser
    {
        public Guid? UserId => Id();
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => Id() is not null;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class FakeCompanies : ITenantStore
    {
        private readonly Dictionary<Guid, TenantInfo> _byId = [];

        public void Add(TenantInfo info) => _byId[info.TenantId] = info;

        public TenantInfo? Find(Guid id) => _byId.GetValueOrDefault(id);

        // A closed company does not resolve, as the real store behaves.
        public Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_byId.GetValueOrDefault(id) is { Status: not TenantStatus.Closed } info ? info : null);

        public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct)
            => Task.FromResult(_byId.Values.FirstOrDefault(t => t.TenantSlug == slug));
    }
}
