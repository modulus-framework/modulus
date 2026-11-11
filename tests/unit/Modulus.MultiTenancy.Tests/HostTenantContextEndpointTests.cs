using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Tenancy;
using Modulus.MultiTenancy.Extensions;
using Modulus.MultiTenancy.Resolvers;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// A platform-operator route (<see cref="HostTenantContextEndpointExtensions.RequireHostTenantContext{TBuilder}"/>) runs in the host
/// context: the host's grants decide. Every other route keeps the company the caller selected, and a company the caller may not enter
/// is still refused on the operator route.
/// </summary>
[Trait("Category", "Unit")]
public sealed class HostTenantContextEndpointTests
{
    private static readonly TenantInfo TenantA = new(Guid.NewGuid(), "tenant-a");
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task An_operator_route_without_a_company_runs_in_the_host_context()
    {
        var (ctx, store) = Build(operatorRoute: true, headerTenantId: null, memberships: []);
        bool? isHost = null;

        await Invoke(ctx, store, c => { isHost = c.RequestServices.GetRequiredService<CurrentTenant>().IsHost; return Task.CompletedTask; });

        isHost.Should().BeTrue();
    }

    [Fact]
    public async Task An_operator_route_does_not_change_the_host_context_for_the_rest_of_the_request()
    {
        var (ctx, store) = Build(operatorRoute: true, headerTenantId: null, memberships: []);
        var tenant = ctx.RequestServices.GetRequiredService<CurrentTenant>();

        await Invoke(ctx, store, _ => Task.CompletedTask);

        tenant.IsHost.Should().BeFalse("the host scope ends with the request");
    }

    [Fact]
    public async Task An_ordinary_route_without_a_company_is_not_the_host()
    {
        var (ctx, store) = Build(operatorRoute: false, headerTenantId: null, memberships: []);
        bool? isHost = null;

        await Invoke(ctx, store, c => { isHost = c.RequestServices.GetRequiredService<CurrentTenant>().IsHost; return Task.CompletedTask; });

        isHost.Should().BeFalse("an unresolved request fails closed");
    }

    [Fact]
    public async Task An_operator_route_still_refuses_a_company_the_caller_may_not_enter()
    {
        var (ctx, store) = Build(operatorRoute: true, headerTenantId: TenantA.TenantId, memberships: []);
        var reached = false;

        await Invoke(ctx, store, _ => { reached = true; return Task.CompletedTask; });

        reached.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task An_operator_route_without_a_user_still_reaches_the_host_scope_only_for_the_authorization_check()
    {
        // The middleware does not authenticate anyone: an anonymous caller reaches the host scope, and the endpoint's
        // authorization policy (not this middleware) refuses it. This pins that the host scope is not an access grant by itself.
        var (ctx, store) = Build(operatorRoute: true, headerTenantId: null, memberships: [], authenticated: false);
        bool? isHost = null;

        await Invoke(ctx, store, c => { isHost = c.RequestServices.GetRequiredService<CurrentTenant>().IsHost; return Task.CompletedTask; });

        isHost.Should().BeTrue();
    }

    private static (HttpContext Ctx, FakeStore Store) Build(
        bool operatorRoute, Guid? headerTenantId, IEnumerable<Guid> memberships, bool authenticated = true)
    {
        var store = new FakeStore(TenantA);
        var membershipStore = new InMemoryTenantMembershipStore();
        foreach (var tenant in memberships)
            membershipStore.Add(UserId, tenant);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(membershipStore);
        services.AddMultiTenancy(t => t.RequireMembership());
        services.AddSingleton<CurrentTenant>();
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (headerTenantId is not null)
            ctx.Request.Headers["X-Tenant-Id"] = headerTenantId.ToString();

        ctx.User = authenticated
            ? new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, UserId.ToString())], "test"))
            : new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());

        if (operatorRoute)
            ctx.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(HostTenantContextMetadata.Instance), "operator"));
        return (ctx, store);
    }

    private static Task Invoke(HttpContext ctx, FakeStore store, RequestDelegate next)
    {
        IEnumerable<ITenantResolver> resolvers = [new HeaderTenantResolver(store), new JwtClaimTenantResolver(store)];
        return new TenantMiddleware(next, resolvers, NullLogger<TenantMiddleware>.Instance).InvokeAsync(ctx);
    }

    private sealed class FakeStore(params TenantInfo[] tenants) : ITenantStore
    {
        public Task<TenantInfo?> FindByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(tenants.FirstOrDefault(t => t.TenantId == id));

        public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken ct)
            => Task.FromResult(tenants.FirstOrDefault(t => t.TenantSlug == slug));
    }
}
