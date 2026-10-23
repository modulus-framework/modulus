using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Management;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Authorization.Grants;
using Xunit;

namespace Modulus.Authorization.Management.Tests;

/// <summary>
/// The role deletion guard (premodule 1, D-P1) against the real EF grant store: a role holding any allow or deny grant is refused,
/// and it is allowed again once every grant is revoked.
/// </summary>
[Trait("Category", "Unit")]
public sealed class RoleDeletionGuardEfTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant, NullCurrentTenant>();
        services.AddModulusAuthorization();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        services.AddModulusAuthorizationManagement();
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task A_role_with_no_grants_may_be_deleted()
    {
        using var scope = _services.CreateScope();
        var guard = scope.ServiceProvider.GetRequiredService<IRoleDeletionGuard>();

        (await guard.BlockingReasonAsync("Auditor", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task A_role_with_an_allow_grant_is_refused_until_it_is_revoked()
    {
        using var scope = _services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<EfPermissionGrantStore>();
        var guard = scope.ServiceProvider.GetRequiredService<IRoleDeletionGuard>();

        await store.GrantToRoleAsync("Auditor", ["ops:read"]);
        var reason = await guard.BlockingReasonAsync("Auditor", CancellationToken.None);
        reason.Should().Contain("1 permission grant");

        await store.RevokeFromRoleAsync("Auditor", "ops:read");
        (await guard.BlockingReasonAsync("Auditor", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task A_role_with_only_a_deny_grant_is_still_refused()
    {
        using var scope = _services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<EfPermissionGrantStore>();
        var guard = scope.ServiceProvider.GetRequiredService<IRoleDeletionGuard>();

        await store.DenyToRoleAsync("Auditor", ["ops:danger"]);

        (await guard.BlockingReasonAsync("Auditor", CancellationToken.None)).Should().Contain("1 permission grant");
    }

    [Fact]
    public async Task Grants_of_other_roles_do_not_block_the_role()
    {
        using var scope = _services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<EfPermissionGrantStore>();
        var guard = scope.ServiceProvider.GetRequiredService<IRoleDeletionGuard>();

        await store.GrantToRoleAsync("Operator", ["ops:read"]);

        (await guard.BlockingReasonAsync("Auditor", CancellationToken.None)).Should().BeNull();
    }
}
