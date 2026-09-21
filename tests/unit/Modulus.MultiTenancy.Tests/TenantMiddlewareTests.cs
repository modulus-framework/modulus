using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Core.Abstractions;
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
        bool includeJwtResolver = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<CurrentTenant>();
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
