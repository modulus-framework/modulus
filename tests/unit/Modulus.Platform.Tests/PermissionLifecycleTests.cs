using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Permission lifecycle: a deprecated permission keeps working for existing grants, a retired one is denied to everyone
/// (grants, wildcards, prerequisites and permission claims alike), and a redeclaration may not change what a permission means.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PermissionLifecycleTests
{
    private static readonly Guid User = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static PermissionRegistry Registry()
    {
        var registry = new PermissionRegistry();
        registry.Add("sales:order:read", "read orders");
        registry.Add("sales:order:legacy-export", "old export", null, PermissionSensitivity.Normal, PermissionLifecycle.Retired, "sales:order:export");
        registry.Add("sales:order:old-view", "old view", null, PermissionSensitivity.Normal, PermissionLifecycle.Deprecated, "sales:order:read");
        registry.Add("sales:order:export", "export orders", ["sales:order:read"]);
        registry.Add("sales:legacy:retired-chain", "retired with prerequisite", ["sales:order:read"], PermissionSensitivity.Normal, PermissionLifecycle.Retired, null);
        registry.Freeze();
        return registry;
    }

    [Fact]
    public void A_retired_permission_is_denied_even_when_granted_directly()
    {
        var store = new InMemoryPermissionGrantStore().GrantToUser(User, "sales:order:legacy-export");

        new PermissionResolver(store, Registry()).Resolve(new PrincipalGrantQuery(User, []))
            .Should().NotContain("sales:order:legacy-export");
    }

    [Fact]
    public void A_wildcard_never_brings_back_a_retired_permission()
    {
        var store = new InMemoryPermissionGrantStore().GrantToUser(User, "sales:*");

        var effective = new PermissionResolver(store, Registry()).Resolve(new PrincipalGrantQuery(User, []));

        effective.Should().Contain("sales:order:read").And.Contain("sales:order:export");
        effective.Should().NotContain("sales:order:legacy-export");
        effective.Should().NotContain("sales:legacy:retired-chain");
    }

    [Fact]
    public void A_retired_permission_confers_none_of_its_prerequisites()
    {
        var store = new InMemoryPermissionGrantStore().GrantToUser(User, "sales:legacy:retired-chain");

        new PermissionResolver(store, Registry()).Resolve(new PrincipalGrantQuery(User, []))
            .Should().BeEmpty();
    }

    [Fact]
    public void A_deprecated_permission_stays_effective_for_an_existing_grant()
    {
        var store = new InMemoryPermissionGrantStore().GrantToUser(User, "sales:order:old-view");

        new PermissionResolver(store, Registry()).Resolve(new PrincipalGrantQuery(User, []))
            .Should().Contain("sales:order:old-view");
    }

    [Fact]
    public void Redeclaring_a_permission_with_the_same_meaning_is_allowed()
    {
        var registry = new PermissionRegistry();
        registry.Add("ops:read", "read");
        registry.Add("ops:read", "read again, new wording", null, PermissionSensitivity.Normal, PermissionLifecycle.Active, null);

        registry.GetAll().Should().ContainSingle(p => p.Permission == "ops:read");
    }

    [Theory]
    [InlineData(PermissionSensitivity.Critical, PermissionLifecycle.Active)]
    [InlineData(PermissionSensitivity.Normal, PermissionLifecycle.Retired)]
    public void Redeclaring_a_permission_with_a_different_meaning_fails_at_startup(PermissionSensitivity sensitivity, PermissionLifecycle lifecycle)
    {
        var registry = new PermissionRegistry();
        registry.Add("ops:read", "read");

        var act = () => registry.Add("ops:read", "read", null, sensitivity, lifecycle, null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ops:read*");
    }

    [Fact]
    public void An_active_permission_cannot_name_a_replacement()
    {
        var registry = new PermissionRegistry();

        var act = () => registry.Add("ops:read", "read", null, PermissionSensitivity.Normal, PermissionLifecycle.Active, "ops:view");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task A_retired_permission_claim_does_not_confer_access_even_when_claims_are_trusted()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusAuthorization(o => o.TrustPermissionClaims = true);
        services.AddPermissions("Lifecycle.Test", r =>
            r.Add("sales:order:legacy-export", "old export", null, PermissionSensitivity.Normal, PermissionLifecycle.Retired, "sales:order:export"));
        using var provider = services.BuildServiceProvider();
        // The declarations reach the registry when the hosted services start, as they do in a running host.
        foreach (var hosted in provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>())
            await hosted.StartAsync(CancellationToken.None);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("permission", "sales:order:legacy-export")], authenticationType: "Test"));

        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(principal, resource: null, "sales:order:legacy-export");

        result.Succeeded.Should().BeFalse();
    }
}
