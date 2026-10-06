using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Governance;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>A delegation's cap follows the delegator's live roles, not the roles they had when it was created.</summary>
[Trait("Category", "Unit")]
public sealed class DelegationRoleRefreshTests : IDisposable
{
    private static readonly Guid Manager = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Deputy = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _provider;
    private readonly FakeDirectory _directory = new();

    private sealed class FakeDirectory : IUserRoleDirectory
    {
        public Dictionary<Guid, string[]> Roles { get; } = [];

        public ValueTask<IReadOnlyCollection<string>?> GetRolesAsync(Guid userId, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyCollection<string>?>(Roles.TryGetValue(userId, out var r) ? r : null);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public DelegationRoleRefreshTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedClock());
        services.AddSingleton<IUserRoleDirectory>(_directory);
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        _provider = services.BuildServiceProvider();

        using var db = _provider.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }

    private async Task<Delegation> DelegateAsync(params string[] roles)
        => await _provider.GetRequiredService<EfDelegationStore>().DelegateAsync(
            Manager, roles, Deputy, ["orders:approve"], Now.AddHours(-1), Now.AddDays(1));

    [Fact]
    public async Task A_role_the_delegator_lost_is_removed_from_the_snapshot()
    {
        await DelegateAsync("Approver", "Clerk");
        _directory.Roles[Manager] = ["Clerk"];

        var changed = await _provider.GetRequiredService<DelegationRoleRefresher>().RefreshAsync();

        changed.Should().Be(1);
        _provider.GetRequiredService<IDelegationStore>().ActiveFor(Deputy, Now).Single().FromRoles
            .Should().BeEquivalentTo(["Clerk"]);
    }

    [Fact]
    public async Task A_delegator_the_identity_store_no_longer_knows_keeps_no_roles()
    {
        await DelegateAsync("Approver");

        await _provider.GetRequiredService<DelegationRoleRefresher>().RefreshAsync();

        _provider.GetRequiredService<IDelegationStore>().ActiveFor(Deputy, Now).Single().FromRoles.Should().BeEmpty();
    }

    [Fact]
    public async Task Unchanged_roles_write_nothing_and_an_access_change_refreshes_only_that_user()
    {
        await DelegateAsync("Approver");
        _directory.Roles[Manager] = ["Approver"];
        var refresher = _provider.GetRequiredService<DelegationRoleRefresher>();

        (await refresher.RefreshAsync()).Should().Be(0);

        _directory.Roles[Manager] = [];
        await refresher.RefreshAsync(Guid.NewGuid());
        _provider.GetRequiredService<IDelegationStore>().ActiveFor(Deputy, Now).Single().FromRoles.Should().ContainSingle();

        foreach (var observer in _provider.GetServices<IAccessChangeObserver>())
            await observer.OnAccessChangedAsync(new AccessChange { Kind = "role", Reason = "test", UserId = Manager });
        _provider.GetRequiredService<IDelegationStore>().ActiveFor(Deputy, Now).Single().FromRoles.Should().BeEmpty();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }
}
