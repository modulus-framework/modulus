using System.Diagnostics;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Governance;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Security;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// TEST-3: how long a delegation's role snapshot can disagree with the identity store. A role change the store reports is applied at once;
/// one it does not report waits for the refresh timer, so the measured staleness is bounded by the timer's interval.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DelegationRoleStalenessTests : IDisposable
{
    private static readonly Guid Manager = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Deputy = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _provider;
    private readonly FakeDirectory _directory = new();

    private sealed class FakeDirectory : IUserRoleDirectory
    {
        public Dictionary<Guid, string[]> Roles { get; } = [];

        public ValueTask<IReadOnlyCollection<string>?> GetRolesAsync(Guid userId, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyCollection<string>?>(Roles.TryGetValue(userId, out var r) ? r : null);

        public ValueTask<bool> MayStartAccessAsync(Guid userId, CancellationToken ct = default)
            => ValueTask.FromResult(Roles.ContainsKey(userId));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public DelegationRoleStalenessTests()
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

    private async Task DelegateAsync(params string[] roles)
        => await _provider.GetRequiredService<EfDelegationStore>().DelegateAsync(
            Manager, roles, Deputy, ["orders:approve"], Now.AddHours(-1), Now.AddDays(1));

    private IReadOnlyCollection<string> SnapshotRoles()
        => _provider.GetRequiredService<IDelegationStore>().ActiveFor(Deputy, Now).Single().FromRoles;

    [Fact]
    public async Task An_unreported_role_change_stays_in_the_snapshot_until_the_next_refresh()
    {
        await DelegateAsync("Approver", "Clerk");
        _directory.Roles[Manager] = ["Clerk"];

        SnapshotRoles().Should().BeEquivalentTo(["Approver", "Clerk"], "nothing has refreshed the snapshot yet");

        await _provider.GetRequiredService<DelegationRoleRefresher>().RefreshAsync();

        SnapshotRoles().Should().BeEquivalentTo(["Clerk"]);
    }

    [Fact]
    public async Task A_reported_role_change_reaches_the_snapshot_at_once()
    {
        await DelegateAsync("Approver", "Clerk");
        _directory.Roles[Manager] = ["Clerk"];

        foreach (var observer in _provider.GetServices<IAccessChangeObserver>())
            await observer.OnAccessChangedAsync(new AccessChange { Kind = "role", Reason = "test", UserId = Manager });

        SnapshotRoles().Should().BeEquivalentTo(["Clerk"]);
    }

    [Fact]
    public async Task The_refresh_timer_bounds_an_unreported_change_to_its_interval()
    {
        await DelegateAsync("Approver", "Clerk");
        _directory.Roles[Manager] = ["Clerk"];
        var service = new DelegationRoleRefreshService(
            _provider.GetRequiredService<DelegationRoleRefresher>(),
            Options.Create(new DelegationRoleRefreshOptions { Interval = TimeSpan.FromMilliseconds(50) }),
            NullLogger<DelegationRoleRefreshService>.Instance);

        var clock = Stopwatch.StartNew();
        await service.StartAsync(CancellationToken.None);
        try
        {
            while (SnapshotRoles().Count != 1 && clock.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(10);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        SnapshotRoles().Should().BeEquivalentTo(["Clerk"]);
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the timer ticks every 50 ms, so the change is picked up well within a second");
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }
}
