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

    [Fact]
    public void GetEffectivePermissions_IsEmpty_WhenUnauthenticated()
    {
        var checker = BuildChecker(
            principal: null,
            seed: s => s.GrantToRole("clerk", "sales:order:read"));

        checker.GetEffectivePermissions().Should().BeEmpty();
    }
}
