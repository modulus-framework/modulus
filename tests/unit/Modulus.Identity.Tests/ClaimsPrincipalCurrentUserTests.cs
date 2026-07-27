using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Identity;
using Modulus.Identity.Extensions;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// <see cref="ICurrentUser.Permissions"/> must reflect the same source of truth
/// as <see cref="ICurrentUser.HasPermission"/> — the server-side grant store via
/// <see cref="IPermissionChecker"/> when one is registered (blueprint §22),
/// falling back to raw "permission" token claims only when it isn't. Before this
/// fix, <c>Permissions</c> always read claims directly regardless of whether a
/// checker was registered — this locks in the fixed behavior.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ClaimsPrincipalCurrentUserTests
{
    private static readonly Guid User = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static ClaimsPrincipal Authenticated(Guid userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static ICurrentUser BuildCurrentUser(
        ClaimsPrincipal? principal,
        bool withGrantStoreChecker,
        Action<Modulus.Authorization.Grants.InMemoryPermissionGrantStore>? seed = null)
    {
        var services = new ServiceCollection();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, ClaimsPrincipalCurrentUser>();

        if (withGrantStoreChecker)
        {
            services.AddModulusAuthorization();
            if (seed is not null)
                services.AddPermissionGrants(seed);
            services.AddGrantStorePermissionChecker();
        }

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            principal is null ? null : new DefaultHttpContext { User = principal };

        // Scoped service — resolve within a scope, mirroring per-request lifetime.
        var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ICurrentUser>();
    }

    [Fact]
    public void Permissions_ResolvesFromTheGrantStore_WhenACheckerIsRegistered()
    {
        var currentUser = BuildCurrentUser(
            Authenticated(User, "clerk"),
            withGrantStoreChecker: true,
            seed: s => s.GrantToRole("clerk", "sales:order:read")
                        .GrantToUser(User, "reports:view"));

        currentUser.Permissions.Should().BeEquivalentTo(["sales:order:read", "reports:view"]);
    }

    [Fact]
    public void Permissions_IgnoresStalePermissionClaims_WhenACheckerIsRegistered()
    {
        // The whole point of the grant-store checker (blueprint §22) is that
        // effective permissions are resolved server-side, not trusted from
        // whatever the token happens to carry — a stale/forged "permission"
        // claim must not leak into Permissions once a checker is registered.
        var principal = Authenticated(User, "clerk");
        principal.AddIdentity(new ClaimsIdentity(
            [new Claim("permission", "finance:ledger:post")]));

        var currentUser = BuildCurrentUser(
            principal,
            withGrantStoreChecker: true,
            seed: s => s.GrantToRole("clerk", "sales:order:read"));

        currentUser.Permissions.Should().BeEquivalentTo(["sales:order:read"]);
        currentUser.Permissions.Should().NotContain("finance:ledger:post");
    }

    [Fact]
    public void Permissions_IsEmpty_WhenUnauthenticatedEvenWithACheckerRegistered()
    {
        var currentUser = BuildCurrentUser(
            principal: null,
            withGrantStoreChecker: true,
            seed: s => s.GrantToRole("clerk", "sales:order:read"));

        currentUser.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void Permissions_FallsBackToTokenClaims_WhenNoCheckerIsRegistered()
    {
        var principal = Authenticated(User);
        principal.AddIdentity(new ClaimsIdentity(
            [new Claim("permission", "sales:order:read"), new Claim("permission", "reports:view")]));

        var currentUser = BuildCurrentUser(principal, withGrantStoreChecker: false);

        currentUser.Permissions.Should().BeEquivalentTo(["sales:order:read", "reports:view"]);
    }
}
