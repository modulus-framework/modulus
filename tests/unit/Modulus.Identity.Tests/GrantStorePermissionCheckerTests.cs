using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Identity;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// End-to-end wiring of the grant-store permission checker: resolves the current
/// principal's effective permissions from the server-side grant store using the
/// principal's role/user claims, through the public <see cref="IPermissionChecker"/>
/// seam that <see cref="ClaimsPrincipalCurrentUser"/> consults.
/// </summary>
[Trait("Category", "Unit")]
public sealed class GrantStorePermissionCheckerTests
{
    private static readonly Guid User = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static IPermissionChecker BuildChecker(
        ClaimsPrincipal? principal,
        Action<Modulus.Authorization.Grants.InMemoryPermissionGrantStore> seed)
    {
        var services = new ServiceCollection();
        services.AddModulusAuthorization();
        services.AddPermissionGrants(seed);
        services.AddGrantStorePermissionChecker();

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            principal is null ? null : new DefaultHttpContext { User = principal };

        // Scoped service — resolve within a scope, mirroring per-request lifetime.
        var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
    }

    /// <summary>A checker resolved inside a background job: no request, and the job's actor.</summary>
    private static IPermissionChecker BuildJobChecker(string? actor, Action<Modulus.Authorization.Grants.InMemoryPermissionGrantStore> seed)
    {
        var services = new ServiceCollection();
        services.AddModulusAuthorization();
        services.AddPermissionGrants(seed);
        services.AddGrantStorePermissionChecker();
        services.AddScoped(_ => new Modulus.BackgroundJobs.JobContext { Actor = actor });

        var scope = services.BuildServiceProvider().CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
    }

    [Fact]
    public void A_job_runs_with_its_initiators_grants()
    {
        var checker = BuildJobChecker(
            $"{Modulus.BackgroundJobs.JobActor.PersonPrefix}{User:N}",
            s => s.GrantToUser(User, "sales:order:delete"));

        checker.HasPermission("sales:order:delete").Should().BeTrue();
    }

    [Fact]
    public void A_system_job_has_no_permissions()
    {
        var checker = BuildJobChecker(
            actor: null,
            seed: s => s.GrantToUser(User, "sales:order:delete"));

        checker.HasPermission("sales:order:delete").Should().BeFalse("a system job has no person whose grants it could use");
    }

    private static ClaimsPrincipal Authenticated(Guid userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    [Fact]
    public void Unauthenticated_HasNoPermissions_FailClosed()
    {
        var checker = BuildChecker(
            principal: null,
            seed: s => s.GrantToRole("clerk", "sales:order:read"));

        checker.HasPermission("sales:order:read").Should().BeFalse();
    }

    [Fact]
    public void RoleClaim_GrantsResolveFromStore()
    {
        var checker = BuildChecker(
            Authenticated(User, "clerk"),
            s => s.GrantToRole("clerk", "sales:order:read"));

        checker.HasPermission("sales:order:read").Should().BeTrue();
        checker.HasPermission("sales:order:delete").Should().BeFalse();
    }

    [Fact]
    public void DirectUserGrant_ResolvesFromStore()
    {
        var checker = BuildChecker(
            Authenticated(User),
            s => s.GrantToUser(User, "reports:view"));

        checker.HasPermission("reports:view").Should().BeTrue();
    }

    [Fact]
    public void DenyOnRole_OverridesAllow()
    {
        var checker = BuildChecker(
            Authenticated(User, "clerk"),
            s => s.GrantToRole("clerk", "sales:order:delete")
                  .DenyToRole("clerk", "sales:order:delete"));

        checker.HasPermission("sales:order:delete").Should().BeFalse();
    }

    [Fact]
    public void PermissionNotGranted_IsDenied()
    {
        var checker = BuildChecker(
            Authenticated(User, "clerk"),
            s => s.GrantToRole("clerk", "sales:order:read"));

        checker.HasPermission("finance:ledger:post").Should().BeFalse();
    }

    [Fact]
    public void GetEffectivePermissions_ReturnsTheFullResolvedSet()
    {
        var checker = BuildChecker(
            Authenticated(User, "clerk"),
            s => s.GrantToRole("clerk", "sales:order:read", "sales:order:update")
                  .GrantToUser(User, "reports:view"));

        checker.GetEffectivePermissions().Should().BeEquivalentTo(
            ["sales:order:read", "sales:order:update", "reports:view"]);
    }

    private static ClaimsPrincipal Acting(Guid userId, string agent, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()), new("act", agent) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    [Fact]
    public void An_agent_token_is_capped_to_what_the_agent_role_holds()
    {
        var checker = BuildChecker(
            Acting(User, "agent:orders-bot", "clerk"),
            s => s.GrantToRole("clerk", "sales:order:read", "sales:order:update")
                  .GrantToRole("agent:orders-bot", "sales:order:read"));

        checker.HasPermission("sales:order:read").Should().BeTrue();
        checker.HasPermission("sales:order:update").Should().BeFalse("the agent role does not hold it");
    }

    [Fact]
    public void An_agent_token_with_a_blank_agent_grants_nothing()
    {
        var checker = BuildChecker(
            Acting(User, " ", "clerk"),
            s => s.GrantToRole("clerk", "sales:order:read"));

        checker.GetEffectivePermissions().Should().BeEmpty();
    }

    [Fact]
    public void Without_the_grant_store_an_agent_token_grants_no_permission_claim()
    {
        var principal = Acting(User, "agent:orders-bot", "clerk");
        principal.AddIdentity(new ClaimsIdentity([new Claim("permission", "sales:order:update")], "Test"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };

        var user = new ClaimsPrincipalCurrentUser(accessor);

        user.HasPermission("sales:order:update").Should().BeFalse("a permission claim is not trusted for an agent");
        user.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void Without_the_grant_store_a_token_permission_claim_is_ignored_by_default()
    {
        var principal = Authenticated(User, "clerk");
        principal.AddIdentity(new ClaimsIdentity([new Claim("permission", "sales:order:update")], "Test"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };

        new ClaimsPrincipalCurrentUser(accessor).HasPermission("sales:order:update").Should().BeFalse(
            "a permission claim on a token is not a grant unless the app opts in (TrustPermissionClaims)");
    }

    [Fact]
    public void GetEffectivePermissions_IsEmpty_WhenUnauthenticated()
    {
        var checker = BuildChecker(
            principal: null,
            seed: s => s.GrantToRole("clerk", "sales:order:read"));

        checker.GetEffectivePermissions().Should().BeEmpty();
    }
}
