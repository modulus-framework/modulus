using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy.Extensions;
using Modulus.MultiTenancy.Resolvers;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// <see cref="TenantMiddleware"/> used to trust whichever resolver matched
/// first with no cross-check, so a caller authenticated to tenant A could
/// send <c>X-Tenant-Id: &lt;B&gt;</c> (the documented default resolver order
/// puts the spoofable header ahead of the JWT claim) and have every query run
/// as tenant B. This asserts the fix: when a <see cref="JwtClaimTenantResolver"/>
/// is configured and the caller is authenticated, its claim-derived tenant is
/// cross-checked against whichever tenant actually resolved, and a mismatch
/// is rejected outright rather than silently trusted.
/// </summary>
/// <remarks>
/// Assertions on the resolved tenant read it from inside <c>next()</c>, not
/// after the awaited <see cref="TenantMiddleware.InvokeAsync"/> call returns:
/// <see cref="CurrentTenant"/>'s AsyncLocal mutation is made inside that
/// method's own async state machine, whose first <c>MoveNext</c> runs under
/// an isolating <c>ExecutionContext.Run</c> (via <c>AsyncTaskMethodBuilder.Start</c>)
/// -- visible to everything called from inside it (downstream middleware,
/// here <c>next()</c>, exactly like a real page handler), but not flowed back
/// out to this test method's own continuation once the awaited call completes.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class TenantMiddlewareTests
{
    private static readonly TenantInfo TenantA = new(Guid.NewGuid(), "tenant-a");
    private static readonly TenantInfo TenantB = new(Guid.NewGuid(), "tenant-b");

    [Fact]
    public async Task Authenticated_caller_spoofing_the_header_to_another_tenant_is_rejected()
    {
        // Header (spoofable) resolves B; the validated JWT "tid" claim says A —
        // the documented default order (header before JWT) would otherwise let
        // the header win.
        var store = new FakeStore(TenantA, TenantB);
        var reached = false;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantB.TenantId,
            claimTenantId: TenantA.TenantId,
            authenticated: true);

        await InvokeAsync(ctx, store, next: _ => { reached = true; return Task.CompletedTask; });

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        reached.Should().BeFalse();
    }

    [Fact]
    public async Task Authenticated_caller_whose_header_matches_their_claim_is_allowed()
    {
        var store = new FakeStore(TenantA, TenantB);
        Guid? seenByNext = null;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantA.TenantId,
            claimTenantId: TenantA.TenantId,
            authenticated: true);

        await InvokeAsync(
            ctx, store,
            next: c => { seenByNext = c.RequestServices.GetRequiredService<CurrentTenant>().TenantId; return Task.CompletedTask; });

        seenByNext.Should().Be(TenantA.TenantId);
    }

    [Fact]
    public async Task Anonymous_caller_is_never_cross_checked_against_a_claim_it_does_not_have()
    {
        var store = new FakeStore(TenantA, TenantB);
        Guid? seenByNext = null;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantB.TenantId,
            claimTenantId: null,
            authenticated: false);

        await InvokeAsync(
            ctx, store,
            next: c => { seenByNext = c.RequestServices.GetRequiredService<CurrentTenant>().TenantId; return Task.CompletedTask; });

        seenByNext.Should().Be(TenantB.TenantId);
    }

    [Theory]
    [InlineData("GET", true)]
    [InlineData("HEAD", true)]
    [InlineData("OPTIONS", true)]
    [InlineData("POST", false)]
    [InlineData("PUT", false)]
    [InlineData("DELETE", false)]
    public async Task A_suspended_company_can_be_read_but_not_changed(string method, bool reaches)
    {
        var suspended = new TenantInfo(Guid.NewGuid(), "suspended", Status: TenantStatus.Suspended);
        var store = new FakeStore(suspended);
        var reached = false;
        var ctx = BuildContext(store, headerTenantId: suspended.TenantId, claimTenantId: null, authenticated: false);
        ctx.Request.Method = method;

        await InvokeAsync(ctx, store, next: _ => { reached = true; return Task.CompletedTask; });

        reached.Should().Be(reaches);
        ctx.Response.StatusCode.Should().Be(reaches ? StatusCodes.Status200OK : StatusCodes.Status423Locked);
    }

    [Fact]
    public async Task Without_a_jwt_claim_resolver_configured_there_is_nothing_to_cross_check_against()
    {
        // Header-only deployment (trusted edge injects X-Tenant-Id) — no
        // JwtClaimTenantResolver in the pipeline, so the header is trusted as
        // documented; this must not regress.
        var store = new FakeStore(TenantA, TenantB);
        Guid? seenByNext = null;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantB.TenantId,
            claimTenantId: null,
            authenticated: true,
            includeJwtResolver: false);

        await InvokeAsync(
            ctx, store,
            next: c => { seenByNext = c.RequestServices.GetRequiredService<CurrentTenant>().TenantId; return Task.CompletedTask; },
            includeJwtResolver: false);

        seenByNext.Should().Be(TenantB.TenantId);
    }

    [Fact]
    public async Task Authenticated_caller_whose_tenant_no_longer_resolves_cannot_fall_back_to_the_header()
    {
        // The token names a tenant the store no longer returns (deactivated or deleted);
        // the cross-check used to be skipped, so the header picked any tenant.
        var store = new FakeStore(TenantB);
        var reached = false;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantB.TenantId,
            claimTenantId: TenantA.TenantId,
            authenticated: true);

        await InvokeAsync(ctx, store, next: _ => { reached = true; return Task.CompletedTask; });

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        reached.Should().BeFalse();
    }

    [Fact]
    public async Task Authenticated_host_account_without_a_tenant_claim_still_selects_a_tenant_by_header()
    {
        var store = new FakeStore(TenantA, TenantB);
        Guid? seenByNext = null;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantB.TenantId,
            claimTenantId: null,
            authenticated: true);

        await InvokeAsync(
            ctx, store,
            next: c => { seenByNext = c.RequestServices.GetRequiredService<CurrentTenant>().TenantId; return Task.CompletedTask; });

        seenByNext.Should().Be(TenantB.TenantId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Host_tenant_access_policy_gates_a_host_account_selecting_a_tenant_by_header(bool holdsPolicy)
    {
        var store = new FakeStore(TenantA, TenantB);
        var reached = false;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantB.TenantId,
            claimTenantId: null,
            authenticated: true,
            configureServices: RequireHostAdmin,
            roles: holdsPolicy ? ["HostAdmin"] : []);

        await InvokeAsync(ctx, store, next: _ => { reached = true; return Task.CompletedTask; });

        reached.Should().Be(holdsPolicy);
        if (!holdsPolicy)
            ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Host_tenant_access_policy_does_not_apply_to_a_tenant_account_in_its_own_tenant()
    {
        var store = new FakeStore(TenantA, TenantB);
        var reached = false;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantA.TenantId,
            claimTenantId: TenantA.TenantId,
            authenticated: true,
            configureServices: RequireHostAdmin);

        await InvokeAsync(ctx, store, next: _ => { reached = true; return Task.CompletedTask; });

        reached.Should().BeTrue();
    }

    [Fact]
    public async Task Host_tenant_access_policy_does_not_apply_to_anonymous_requests()
    {
        var store = new FakeStore(TenantA, TenantB);
        var reached = false;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantB.TenantId,
            claimTenantId: null,
            authenticated: false,
            configureServices: RequireHostAdmin);

        await InvokeAsync(ctx, store, next: _ => { reached = true; return Task.CompletedTask; });

        reached.Should().BeTrue();
    }

    private static void RequireHostAdmin(IServiceCollection services)
    {
        services.AddLogging();
        services.AddAuthorization(o => o.AddPolicy("TenantSwitch", p => p.RequireRole("HostAdmin")));
        services.AddMultiTenancy(t => t.RequireHostTenantAccessPolicy("TenantSwitch"));
    }

    // ── Membership (company = tenant, one login across companies) ─────────

    private static readonly Guid UserId = Guid.NewGuid();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task With_membership_required_an_account_without_a_tenant_claim_enters_only_its_member_tenants(bool isMember)
    {
        var store = new FakeStore(TenantA, TenantB);
        var memberships = new InMemoryTenantMembershipStore();
        if (isMember)
            memberships.Add(UserId, TenantB.TenantId);
        memberships.Add(UserId, TenantA.TenantId);
        var reached = false;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantB.TenantId,
            claimTenantId: null,
            authenticated: true,
            configureServices: s => RequireMembership(s, memberships),
            userId: UserId);

        await InvokeAsync(ctx, store, next: _ => { reached = true; return Task.CompletedTask; });

        reached.Should().Be(isMember);
        if (!isMember)
            ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task With_membership_required_an_account_without_a_user_id_is_rejected()
    {
        var store = new FakeStore(TenantA);
        var reached = false;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantA.TenantId,
            claimTenantId: null,
            authenticated: true,
            configureServices: s => RequireMembership(s, new InMemoryTenantMembershipStore()));

        await InvokeAsync(ctx, store, next: _ => { reached = true; return Task.CompletedTask; });

        reached.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task With_membership_required_a_revoked_membership_closes_entry_on_the_next_request()
    {
        var store = new FakeStore(TenantA);
        var memberships = new InMemoryTenantMembershipStore();
        memberships.Add(UserId, TenantA.TenantId);
        memberships.Remove(UserId, TenantA.TenantId);
        var reached = false;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantA.TenantId,
            claimTenantId: null,
            authenticated: true,
            configureServices: s => RequireMembership(s, memberships),
            userId: UserId);

        await InvokeAsync(ctx, store, next: _ => { reached = true; return Task.CompletedTask; });

        reached.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task With_membership_required_the_host_policy_is_the_break_glass_for_a_non_member(bool holdsPolicy)
    {
        var store = new FakeStore(TenantA);
        var reached = false;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantA.TenantId,
            claimTenantId: null,
            authenticated: true,
            configureServices: s =>
            {
                RequireMembership(s, new InMemoryTenantMembershipStore());
                s.AddAuthorization(o => o.AddPolicy("TenantSwitch", p => p.RequireRole("HostAdmin")));
                s.AddMultiTenancy(t => t.RequireHostTenantAccessPolicy("TenantSwitch"));
            },
            userId: UserId,
            roles: holdsPolicy ? ["HostAdmin"] : []);

        await InvokeAsync(ctx, store, next: _ => { reached = true; return Task.CompletedTask; });

        reached.Should().Be(holdsPolicy);
    }

    [Fact]
    public async Task With_membership_required_a_tenant_claim_still_pins_the_token()
    {
        // A member of B whose token is pinned to A cannot use the header to reach B.
        var store = new FakeStore(TenantA, TenantB);
        var memberships = new InMemoryTenantMembershipStore();
        memberships.Add(UserId, TenantB.TenantId);
        var reached = false;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantB.TenantId,
            claimTenantId: TenantA.TenantId,
            authenticated: true,
            configureServices: s => RequireMembership(s, memberships),
            userId: UserId);

        await InvokeAsync(ctx, store, next: _ => { reached = true; return Task.CompletedTask; });

        reached.Should().BeFalse();
    }

    [Fact]
    public async Task With_membership_required_anonymous_requests_are_unaffected()
    {
        var store = new FakeStore(TenantA);
        var reached = false;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantA.TenantId,
            claimTenantId: null,
            authenticated: false,
            configureServices: s => RequireMembership(s, new InMemoryTenantMembershipStore()));

        await InvokeAsync(ctx, store, next: _ => { reached = true; return Task.CompletedTask; });

        reached.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, "tenant.not-a-member", SecurityAuditOutcomes.Denied)]
    [InlineData(true, "tenant.break-glass", SecurityAuditOutcomes.Overridden)]
    public async Task Rejections_and_break_glass_entries_are_recorded_in_the_tenants_security_audit(
        bool holdsPolicy, string action, string outcome)
    {
        var store = new FakeStore(TenantA);
        var audit = new RecordingAuditLog();
        var ctx = BuildContext(
            store,
            headerTenantId: TenantA.TenantId,
            claimTenantId: null,
            authenticated: true,
            configureServices: s =>
            {
                RequireMembership(s, new InMemoryTenantMembershipStore());
                s.AddSingleton<ISecurityAuditLog>(audit);
                s.AddAuthorization(o => o.AddPolicy("TenantSwitch", p => p.RequireRole("HostAdmin")));
                s.AddMultiTenancy(t => t.RequireHostTenantAccessPolicy("TenantSwitch"));
            },
            userId: UserId,
            roles: holdsPolicy ? ["HostAdmin"] : []);

        await InvokeAsync(ctx, store, next: _ => Task.CompletedTask);

        var recorded = audit.Events.Should().ContainSingle().Subject;
        recorded.Action.Should().Be(action);
        recorded.Outcome.Should().Be(outcome);
        recorded.TenantId.Should().Be(TenantA.TenantId, "the company that was reached keeps the record");
        recorded.Actor.Should().Be(UserId.ToString());
    }

    private sealed class RecordingAuditLog : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];

        public void Record(SecurityAuditEvent auditEvent) => Events.Add(auditEvent);
    }

    [Fact]
    public async Task By_default_an_account_without_a_tenant_claim_cannot_select_a_tenant_it_is_not_a_member_of()
    {
        // No RequireMembership() call: membership is the default, so a header alone never opens another company.
        var store = new FakeStore(TenantA, TenantB);
        var reached = false;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantB.TenantId,
            claimTenantId: null,
            authenticated: true,
            configureServices: s =>
            {
                s.AddLogging();
                s.AddMultiTenancy();
            },
            userId: UserId);

        await InvokeAsync(ctx, store, next: _ => { reached = true; return Task.CompletedTask; });

        reached.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Unrestricted_tenant_selection_is_an_explicit_opt_out()
    {
        var store = new FakeStore(TenantA, TenantB);
        var reached = false;
        var ctx = BuildContext(
            store,
            headerTenantId: TenantB.TenantId,
            claimTenantId: null,
            authenticated: true,
            configureServices: s =>
            {
                s.AddLogging();
                s.AddMultiTenancy(t => t.AllowUnrestrictedTenantSelection());
            },
            userId: UserId);

        await InvokeAsync(ctx, store, next: _ => { reached = true; return Task.CompletedTask; });

        reached.Should().BeTrue();
    }

    private static void RequireMembership(IServiceCollection services, InMemoryTenantMembershipStore memberships)
    {
        services.AddLogging();
        services.AddSingleton(memberships);
        services.AddMultiTenancy(t => t.RequireMembership());
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static Task InvokeAsync(
        HttpContext ctx, ITenantStore store, RequestDelegate next, bool includeJwtResolver = true)
    {
        IEnumerable<ITenantResolver> resolvers = includeJwtResolver
            ? [new HeaderTenantResolver(store), new JwtClaimTenantResolver(store)]
            : [new HeaderTenantResolver(store)];

        var middleware = new TenantMiddleware(next, resolvers, NullLogger<TenantMiddleware>.Instance);
        return middleware.InvokeAsync(ctx);
    }

    private static HttpContext BuildContext(
        ITenantStore store,
        Guid? headerTenantId,
        Guid? claimTenantId,
        bool authenticated,
        bool includeJwtResolver = true,
        Action<IServiceCollection>? configureServices = null,
        Guid? userId = null,
        params string[] roles)
    {
        var services = new ServiceCollection();
        services.AddSingleton<CurrentTenant>();
        configureServices?.Invoke(services);
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        if (headerTenantId is not null)
        {
            ctx.Request.Headers["X-Tenant-Id"] = headerTenantId.ToString();
        }

        var claims = new List<Claim>();
        if (claimTenantId is not null)
        {
            claims.Add(new Claim("tid", claimTenantId.ToString()!));
        }

        if (userId is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.ToString()!));
        }

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        ctx.User = authenticated
            ? new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            : new ClaimsPrincipal(new ClaimsIdentity());

        return ctx;
    }

    private sealed class FakeStore(params TenantInfo[] tenants) : ITenantStore
    {
        public Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(tenants.FirstOrDefault(t => t.TenantId == id));

        public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct)
            => Task.FromResult(tenants.FirstOrDefault(t => t.TenantSlug == slug));
    }
}
