using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.Authorization.Organization;
using Modulus.Authorization.Scopes;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

namespace Modulus.Authorization.Management.Tests;

/// <summary>
/// TEST-2 (HTTP): a bulk action is all-or-nothing and an export reaches no further than reading, through real endpoints that call the
/// scope helpers. The caller's grants are the in-memory grant store; the identity is a test header.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ScopeBulkAndExportHttpTests : IAsyncLifetime
{
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private OrderStore _orders = null!;
    private InMemoryPermissionGrantStore _grants = null!;

    public sealed class ScopedOrder : IHasOwner, IHasOrgUnit
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; init; } = "";
        public Guid OwnerId { get; init; }
        public Guid OrgUnitId { get; init; }
        public Guid CustomerId { get; init; }
        public bool Approved { get; set; }
    }

    public sealed record ImportRow(Guid Id, string Name, Guid OwnerId, Guid OrgUnitId, Guid CustomerId);

    private sealed class OrderStore
    {
        public List<ScopedOrder> Items { get; } = [];
    }

    private sealed class HttpUser(IHttpContextAccessor accessor) : ICurrentUser
    {
        private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

        public Guid? UserId => Guid.TryParse(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }

    private sealed class NoDataScope : ICurrentDataScope
    {
        public bool IsUnrestricted => false;
        public IReadOnlyCollection<Guid> OrgUnitIds => [];
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-UserId", out var userId))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    private static readonly Guid s_sales = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid s_finance = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid s_acme = Guid.Parse("cccccccc-0000-0000-0000-00000000000a");

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpUser>();
        builder.Services.AddSingleton<ICurrentDataScope, NoDataScope>();
        builder.Services.AddModulusAuthorization(o => o.TrustPermissionClaims = false);
        builder.Services.AddOrganization();
        builder.Services.AddPermissions("Orders", registry =>
        {
            registry.Add("orders:read", "Read orders.");
            registry.Add("orders:export", "Export orders.");
            registry.Add("orders:approve", "Approve orders.");
            registry.Add("orders:import", "Import orders.");
        });
        builder.Services.AddScopeMap<ScopedOrder>(m => m.Owner(o => o.OwnerId).OrgUnit(o => o.OrgUnitId).AssignedKey("customer", o => o.CustomerId));
        builder.Services.AddSingleton<OrderStore>();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _orders = _app.Services.GetRequiredService<OrderStore>();
        _grants = _app.Services.GetRequiredService<InMemoryPermissionGrantStore>();
        _orders.Items.AddRange(
        [
            new ScopedOrder { Name = "alice-sales-acme", OwnerId = Alice, OrgUnitId = s_sales, CustomerId = s_acme },
            new ScopedOrder { Name = "bob-finance-acme", OwnerId = Bob, OrgUnitId = s_finance, CustomerId = s_acme },
            new ScopedOrder { Name = "bob-sales-acme", OwnerId = Bob, OrgUnitId = s_sales, CustomerId = s_acme },
        ]);

        _app.MapGet("/orders", (IScopeEnforcer scopes, OrderStore store) =>
            Results.Ok(scopes.Apply(store.Items.AsQueryable(), "orders:read").Select(o => o.Name).ToList())).RequireAuthorization();
        _app.MapGet("/orders/export", (IScopeEnforcer scopes, OrderStore store) =>
        {
            try
            {
                return Results.Ok(scopes.ForExport(store.Items.AsQueryable(), "orders:export", "orders:read").Select(o => o.Name).ToList());
            }
            catch (ForbiddenException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
        }).RequireAuthorization();
        _app.MapPost("/orders/approve-all", (Guid[] ids, IScopeEnforcer scopes, OrderStore store) =>
        {
            var batch = store.Items.Where(o => ids.Contains(o.Id)).ToList();
            try
            {
                scopes.EnsureAllInScope(batch, "orders:approve");
            }
            catch (ForbiddenException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            foreach (var order in batch)
                order.Approved = true;
            return Results.Ok(batch.Count);
        }).RequireAuthorization();

        // A related lookup: the orders of one customer. It must apply the same read scope as the list, or it leaks the others.
        _app.MapGet("/customers/{customer:guid}/orders", (Guid customer, IScopeEnforcer scopes, OrderStore store) =>
            Results.Ok(scopes.Apply(store.Items.Where(o => o.CustomerId == customer).AsQueryable(), "orders:read").Select(o => o.Name).ToList()))
            .RequireAuthorization();

        // A bulk import: rows are upserted by id. A row that would overwrite an order outside the caller's scope, or create one outside it,
        // refuses the whole batch, so an import cannot change a record the caller cannot see.
        _app.MapPost("/orders/import", (ImportRow[] rows, IScopeEnforcer scopes, OrderStore store) =>
        {
            var incoming = rows.Select(r => new ScopedOrder { Id = r.Id, Name = r.Name, OwnerId = r.OwnerId, OrgUnitId = r.OrgUnitId, CustomerId = r.CustomerId }).ToList();
            var existing = store.Items.Where(o => rows.Any(r => r.Id == o.Id)).ToList();
            try
            {
                scopes.EnsureAllInScope(existing, "orders:import");
                scopes.EnsureAllInScope(incoming, "orders:import");
            }
            catch (ForbiddenException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            store.Items.RemoveAll(o => rows.Any(r => r.Id == o.Id));
            store.Items.AddRange(incoming);
            return Results.Ok(incoming.Count);
        }).RequireAuthorization();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private HttpClient As(Guid user)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", user.ToString());
        return client;
    }

    private void Grant(Guid user, string permission, PermissionScope scope)
        => _grants.Add(new PermissionGrant(GrantHolderType.User, user.ToString(), permission, PermissionGrantType.Allow, scope));

    [Fact]
    public async Task A_bulk_approval_of_orders_all_in_scope_goes_through()
    {
        Grant(Bob, "orders:approve", PermissionScope.Own);
        var own = _orders.Items.Where(o => o.OwnerId == Bob).Select(o => o.Id).ToArray();

        var response = await As(Bob).PostAsJsonAsync("/orders/approve-all", own);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _orders.Items.Where(o => o.OwnerId == Bob).Should().OnlyContain(o => o.Approved);
    }

    [Fact]
    public async Task One_foreign_order_in_a_bulk_approval_refuses_the_whole_batch()
    {
        Grant(Bob, "orders:approve", PermissionScope.Own);
        var batch = _orders.Items.Select(o => o.Id).ToArray();

        var response = await As(Bob).PostAsJsonAsync("/orders/approve-all", batch);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _orders.Items.Should().NotContain(o => o.Approved, "nothing in the batch is approved, not even the caller's own orders");
    }

    [Fact]
    public async Task An_export_returns_only_what_the_caller_can_read()
    {
        Grant(Bob, "orders:read", PermissionScope.Own);
        Grant(Bob, "orders:export", PermissionScope.Own);

        var exported = await As(Bob).GetFromJsonAsync<List<string>>("/orders/export");
        var listed = await As(Bob).GetFromJsonAsync<List<string>>("/orders");

        exported.Should().BeEquivalentTo(["bob-finance-acme", "bob-sales-acme"]);
        listed.Should().Contain(exported!, "an export never returns a row the caller's list would not show");
    }

    [Fact]
    public async Task An_export_needs_the_read_permission_as_well()
    {
        Grant(Bob, "orders:export", PermissionScope.Own);

        var response = await As(Bob).GetAsync("/orders/export");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_related_lookup_returns_only_the_orders_the_caller_can_read()
    {
        Grant(Bob, "orders:read", PermissionScope.Own);

        var orders = await As(Bob).GetFromJsonAsync<List<string>>($"/customers/{s_acme}/orders");

        orders.Should().BeEquivalentTo(["bob-finance-acme", "bob-sales-acme"], "Alice's order for the same customer is out of Bob's scope");
    }

    [Fact]
    public async Task An_import_of_the_callers_own_rows_goes_through()
    {
        Grant(Bob, "orders:import", PermissionScope.Own);
        var before = _orders.Items.Count;
        var rows = new[] { new ImportRow(Guid.NewGuid(), "bob-new-1", Bob, s_sales, s_acme), new ImportRow(Guid.NewGuid(), "bob-new-2", Bob, s_finance, s_acme) };

        var response = await As(Bob).PostAsJsonAsync("/orders/import", rows);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _orders.Items.Count.Should().Be(before + 2);
    }

    [Fact]
    public async Task An_import_row_owned_by_someone_else_refuses_the_whole_import()
    {
        Grant(Bob, "orders:import", PermissionScope.Own);
        var before = _orders.Items.Count;
        var rows = new[] { new ImportRow(Guid.NewGuid(), "bob-new", Bob, s_sales, s_acme), new ImportRow(Guid.NewGuid(), "for-alice", Alice, s_sales, s_acme) };

        var response = await As(Bob).PostAsJsonAsync("/orders/import", rows);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _orders.Items.Count.Should().Be(before, "not even the caller's own row is written");
    }

    [Fact]
    public async Task An_import_cannot_overwrite_an_order_outside_the_callers_scope()
    {
        Grant(Bob, "orders:import", PermissionScope.Own);
        var alices = _orders.Items.Single(o => o.Name == "alice-sales-acme");
        var rows = new[] { new ImportRow(alices.Id, "overwritten", Bob, s_sales, s_acme) };

        var response = await As(Bob).PostAsJsonAsync("/orders/import", rows);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _orders.Items.Should().Contain(o => o.Id == alices.Id && o.Name == "alice-sales-acme");
    }
}
