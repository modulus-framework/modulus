using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Xunit;

namespace Modulus.Platform.Tests;

// The ':'-permission policy convention end-to-end: policy provider →
// PermissionRequirement → handler → grant store. HTTP-layer decisions must be
// server-resolved (a runtime grant or revocation takes effect on the next
// check, deny-override applies) with token permission claims as a second source.
[Trait("Category", "Unit")]
public sealed class PermissionPolicyTests
{
    private static readonly Guid User = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static ServiceProvider BuildProvider(
        Action<InMemoryPermissionGrantStore>? grants = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusAuthorization();
        if (grants is not null)
            services.AddPermissionGrants(grants);
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal Authenticated(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Test"));

    private static Task<AuthorizationResult> AuthorizeAsync(
        ServiceProvider provider, ClaimsPrincipal principal, string permission)
        => provider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(principal, resource: null, permission);

    [Fact]
    public async Task Role_grant_in_the_store_satisfies_the_permission_policy()
    {
        using var provider = BuildProvider(s => s.GrantToRole("clerk", "orders:read"));
        var principal = Authenticated(
            new Claim(ClaimTypes.NameIdentifier, User.ToString()),
            new Claim(ClaimTypes.Role, "clerk"));

        (await AuthorizeAsync(provider, principal, "orders:read"))
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Runtime_revocation_takes_effect_on_the_next_check()
    {
        using var provider = BuildProvider(s => s.GrantToRole("clerk", "orders:read"));
        var principal = Authenticated(new Claim(ClaimTypes.Role, "clerk"));

        (await AuthorizeAsync(provider, principal, "orders:read"))
            .Succeeded.Should().BeTrue();

        var store = (InMemoryPermissionGrantStore)provider
            .GetRequiredService<IPermissionGrantStore>();
        store.RevokeFromRole("clerk", "orders:read");

        (await AuthorizeAsync(provider, principal, "orders:read"))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task User_deny_overrides_a_role_allow()
    {
        using var provider = BuildProvider(s => s
            .GrantToRole("clerk", "orders:read")
            .DenyToUser(User, "orders:read"));
        var principal = Authenticated(
            new Claim(ClaimTypes.NameIdentifier, User.ToString()),
            new Claim(ClaimTypes.Role, "clerk"));

        (await AuthorizeAsync(provider, principal, "orders:read"))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Direct_user_grant_resolves_from_the_sub_claim()
    {
        using var provider = BuildProvider(s => s.GrantToUser(User, "reports:view"));
        var principal = Authenticated(new Claim("sub", User.ToString()));

        (await AuthorizeAsync(provider, principal, "reports:view"))
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Token_permission_claim_is_honoured_without_a_store_grant()
    {
        using var provider = BuildProvider();
        var principal = Authenticated(new Claim("permission", "orders:read"));

        (await AuthorizeAsync(provider, principal, "orders:read"))
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Store_deny_overrides_a_stale_token_permission_claim()
    {
        using var provider = BuildProvider(s => s.DenyToUser(User, "orders:read"));
        var principal = Authenticated(
            new Claim(ClaimTypes.NameIdentifier, User.ToString()),
            new Claim("permission", "orders:read"));

        (await AuthorizeAsync(provider, principal, "orders:read"))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Store_wildcard_deny_overrides_a_stale_token_permission_claim()
    {
        using var provider = BuildProvider(s => s.DenyToUser(User, "orders:*"));
        var principal = Authenticated(
            new Claim(ClaimTypes.NameIdentifier, User.ToString()),
            new Claim("permission", "orders:read"));

        (await AuthorizeAsync(provider, principal, "orders:read"))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Unauthenticated_principal_is_denied_even_with_matching_claims()
    {
        using var provider = BuildProvider(s => s.GrantToRole("clerk", "orders:read"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "clerk"), new Claim("permission", "orders:read")]));

        (await AuthorizeAsync(provider, principal, "orders:read"))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_permission_is_denied_fail_closed()
    {
        using var provider = BuildProvider(s => s.GrantToRole("clerk", "orders:read"));
        var principal = Authenticated(new Claim(ClaimTypes.Role, "clerk"));

        (await AuthorizeAsync(provider, principal, "orders:delete"))
            .Succeeded.Should().BeFalse();
    }
}
