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
using Modulus.Authorization.Fields;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Abstractions.Exceptions;
using Xunit;

namespace Modulus.Authorization.Management.Tests;

/// <summary>
/// TEST-2 (HTTP, classified fields): a restricted field is masked in the response of a caller without its read permission, and shown to
/// a caller who holds it. The endpoint reads the record and applies <see cref="IFieldAuthorizer.Redact{T}"/> at the response boundary.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ClassifiedFieldsHttpTests : IAsyncLifetime
{
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid Ann = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private WebApplication _app = null!;
    private InMemoryPermissionGrantStore _grants = null!;
    private Dictionary<Guid, FieldOrder> _orders = null!;
    private Guid _orderId;

    public sealed class FieldOrder
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        [Classified(FieldClassification.Restricted)]
        public decimal Margin { get; set; }
    }

    // A change sets only the fields it names; a null field is not written.
    public sealed record FieldOrderChange(string? Name, decimal? Margin);

    public sealed record FieldOrderRow(Guid Id, FieldOrderChange Change);

    // As in production, the permissions a caller holds are resolved on the server from the grant store, never read from a token.
    private sealed class HttpUser(IHttpContextAccessor accessor, PermissionResolver resolver) : ICurrentUser
    {
        private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

        public Guid? UserId => Guid.TryParse(Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => UserId is { } id && Permissions.Contains(permission);
        public IReadOnlyList<string> Permissions => UserId is { } id
            ? [.. resolver.Resolve(new PrincipalGrantQuery(id, [])).OrderBy(p => p, StringComparer.Ordinal)]
            : [];
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

    public async Task InitializeAsync()
    {
        var profile = FieldSecurityProfile.Define(p => p.Classification(FieldClassification.Restricted, read: "pay:read", write: "pay:write"));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
        builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpUser>();
        builder.Services.AddModulusAuthorization(o => o.TrustPermissionClaims = false);
        builder.Services.AddPermissions("Payments", registry =>
        {
            registry.Add("pay:read", "Read payment figures.");
            registry.Add("pay:write", "Change payment figures.");
        });
        builder.Services.AddFieldSecurity<FieldOrder>(profile);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _grants = _app.Services.GetRequiredService<InMemoryPermissionGrantStore>();
        _orderId = Guid.NewGuid();
        _orders = new Dictionary<Guid, FieldOrder> { [_orderId] = new FieldOrder { Id = _orderId, Name = "Acme order", Margin = 1234.5m } };

        _app.MapGet("/field-orders/{id:guid}", (Guid id, IFieldAuthorizer authorizer) =>
        {
            if (!_orders.TryGetValue(id, out var stored))
                return Results.NotFound();

            // A copy per request: the response is masked, the stored record is not.
            var response = new FieldOrder { Id = stored.Id, Name = stored.Name, Margin = stored.Margin };
            return Results.Ok(authorizer.Redact(response));
        }).RequireAuthorization();

        // An update checks the fields the change sets before it writes anything.
        _app.MapPut("/field-orders/{id:guid}", async (Guid id, FieldOrderChange change, IFieldAuthorizer authorizer, CancellationToken ct) =>
        {
            if (!_orders.TryGetValue(id, out var order))
                return Results.NotFound();
            try
            {
                await authorizer.EnsureWriteAllowedAsync(typeof(FieldOrder), FieldsSetBy(change), ct);
            }
            catch (ForbiddenException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            Apply(order, change);
            return Results.NoContent();
        }).RequireAuthorization();

        // A batch import is all-or-nothing: every row's fields are checked before any row is written.
        _app.MapPost("/field-orders/import", async (FieldOrderRow[] rows, IFieldAuthorizer authorizer, CancellationToken ct) =>
        {
            try
            {
                foreach (var row in rows)
                    await authorizer.EnsureWriteAllowedAsync(typeof(FieldOrder), FieldsSetBy(row.Change), ct);
            }
            catch (ForbiddenException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            foreach (var row in rows)
            {
                if (!_orders.TryGetValue(row.Id, out var order))
                    _orders[row.Id] = order = new FieldOrder { Id = row.Id };
                Apply(order, row.Change);
            }

            return Results.Ok(rows.Length);
        }).RequireAuthorization();

        await _app.StartAsync();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private static List<string> FieldsSetBy(FieldOrderChange change)
    {
        var fields = new List<string>();
        if (change.Name is not null)
            fields.Add(nameof(FieldOrder.Name));
        if (change.Margin is not null)
            fields.Add(nameof(FieldOrder.Margin));
        return fields;
    }

    private static void Apply(FieldOrder order, FieldOrderChange change)
    {
        if (change.Name is not null)
            order.Name = change.Name;
        if (change.Margin is not null)
            order.Margin = change.Margin.Value;
    }

    private HttpClient As(Guid user)
    {
        var client = _app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", user.ToString());
        return client;
    }

    [Fact]
    public async Task A_caller_without_the_read_permission_gets_the_restricted_field_masked()
    {
        var response = await As(Bob).GetAsync($"/field-orders/{_orderId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await response.Content.ReadFromJsonAsync<FieldOrder>();
        order!.Name.Should().Be("Acme order", "the unrestricted fields are still shown");
        order.Margin.Should().Be(0m, "the restricted field is masked without pay:read");
    }

    [Fact]
    public async Task A_caller_with_the_read_permission_sees_the_restricted_field()
    {
        _grants.Add(new PermissionGrant(GrantHolderType.User, Ann.ToString(), "pay:read", PermissionGrantType.Allow));

        var order = await (await As(Ann).GetAsync($"/field-orders/{_orderId}")).Content.ReadFromJsonAsync<FieldOrder>();

        order!.Margin.Should().Be(1234.5m);
    }

    [Fact]
    public async Task A_caller_with_the_write_permission_changes_the_restricted_field()
    {
        _grants.Add(new PermissionGrant(GrantHolderType.User, Ann.ToString(), "pay:read", PermissionGrantType.Allow));
        _grants.Add(new PermissionGrant(GrantHolderType.User, Ann.ToString(), "pay:write", PermissionGrantType.Allow));

        var response = await As(Ann).PutAsJsonAsync($"/field-orders/{_orderId}", new FieldOrderChange(null, 999m));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var order = await (await As(Ann).GetAsync($"/field-orders/{_orderId}")).Content.ReadFromJsonAsync<FieldOrder>();
        order!.Margin.Should().Be(999m);
    }

    [Fact]
    public async Task A_caller_without_the_write_permission_is_refused_the_restricted_field_and_nothing_changes()
    {
        var response = await As(Bob).PutAsJsonAsync($"/field-orders/{_orderId}", new FieldOrderChange("renamed", 1m));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the change sets a restricted field");
        _orders[_orderId].Name.Should().Be("Acme order", "the refused change writes none of its fields, not even the public one");
        _orders[_orderId].Margin.Should().Be(1234.5m);
    }

    [Fact]
    public async Task A_caller_without_the_write_permission_may_still_change_an_unrestricted_field()
    {
        var response = await As(Bob).PutAsJsonAsync($"/field-orders/{_orderId}", new FieldOrderChange("renamed", null));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _orders[_orderId].Name.Should().Be("renamed");
    }

    [Fact]
    public async Task An_import_with_one_restricted_row_refused_writes_no_row()
    {
        var newId = Guid.NewGuid();
        var rows = new[]
        {
            new FieldOrderRow(_orderId, new FieldOrderChange("imported", null)),
            new FieldOrderRow(newId, new FieldOrderChange("new", 5m)),
        };

        var response = await As(Bob).PostAsJsonAsync("/field-orders/import", rows);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _orders[_orderId].Name.Should().Be("Acme order", "the public field of the first row is not written either");
        _orders.Should().NotContainKey(newId);
    }

    [Fact]
    public async Task An_import_by_a_caller_with_the_write_permission_writes_every_row()
    {
        _grants.Add(new PermissionGrant(GrantHolderType.User, Ann.ToString(), "pay:write", PermissionGrantType.Allow));
        var newId = Guid.NewGuid();
        var rows = new[]
        {
            new FieldOrderRow(_orderId, new FieldOrderChange("imported", null)),
            new FieldOrderRow(newId, new FieldOrderChange("new", 5m)),
        };

        var response = await As(Ann).PostAsJsonAsync("/field-orders/import", rows);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _orders[_orderId].Name.Should().Be("imported");
        _orders[newId].Margin.Should().Be(5m);
    }

    [Fact]
    public async Task An_anonymous_caller_gets_no_order_at_all()
    {
        var response = await _app.GetTestClient().GetAsync($"/field-orders/{_orderId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
