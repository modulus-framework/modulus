using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// TEST-3: how long a second node keeps serving an org tree that another node has moved. Each node is its own container over one
/// shared database file, as two replicas are. <c>Invalidate</c> clears only the node that wrote, so the other node's snapshot lasts
/// until <c>CacheDuration</c> has passed on the clock.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OrgHierarchyStalenessTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"modulus-org-stale-{Guid.NewGuid():N}.db");
    private readonly ManualClock _clock = new();
    private readonly List<ServiceProvider> _nodes = [];

    private static readonly Guid s_root = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid s_first = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    private static readonly Guid s_second = Guid.Parse("a0000000-0000-0000-0000-000000000003");
    private static readonly Guid s_child = Guid.Parse("a0000000-0000-0000-0000-000000000004");

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    public void Dispose()
    {
        foreach (var node in _nodes)
            node.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    // One replica: its own container, the same database file, the same clock.
    private ServiceProvider Node()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite($"Data Source={_databasePath}"));
        var provider = services.BuildServiceProvider();
        _nodes.Add(provider);
        return provider;
    }

    private async Task<(EfOrgHierarchy Writer, EfOrgHierarchy Reader)> TwoNodesAsync()
    {
        var writerNode = Node();
        using (var db = writerNode.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext())
            db.Database.EnsureCreated();

        var writer = writerNode.GetRequiredService<EfOrgHierarchy>();
        await writer.AddUnitAsync(s_root, []);
        await writer.AddUnitAsync(s_first, [s_root]);
        await writer.AddUnitAsync(s_second, [s_root]);
        await writer.AddUnitAsync(s_child, [s_first]);

        var reader = Node().GetRequiredService<EfOrgHierarchy>();
        return (writer, reader);
    }

    [Fact]
    public async Task A_move_reaches_the_other_node_only_when_its_cached_snapshot_expires()
    {
        var (writer, reader) = await TwoNodesAsync();
        reader.Ancestors(s_child).Should().Contain(s_first, "the reader loads the tree before the move");

        await writer.MoveUnitAsync(s_child, [s_second]);

        writer.Ancestors(s_child).Should().Contain(s_second, "the node that wrote invalidates its own snapshot");
        reader.Ancestors(s_child).Should().Contain(s_first, "the reader still serves its snapshot from before the move");

        _clock.Advance(TimeSpan.FromSeconds(29));
        reader.Ancestors(s_child).Should().Contain(s_first, "the snapshot is still inside its 30-second window at 29 seconds");

        _clock.Advance(TimeSpan.FromSeconds(2));
        reader.Ancestors(s_child).Should().Contain(s_second, "the snapshot expired at 30 seconds, so the move is visible");
        reader.Ancestors(s_child).Should().NotContain(s_first);
    }

    [Fact]
    public async Task A_second_node_that_has_not_loaded_yet_sees_the_move_at_once()
    {
        var (writer, _) = await TwoNodesAsync();
        await writer.MoveUnitAsync(s_child, [s_second]);

        // A node that loads its first snapshot after the write reads the database, so it has no stale window.
        var late = Node().GetRequiredService<EfOrgHierarchy>();

        late.Ancestors(s_child).Should().Contain(s_second).And.NotContain(s_first);
    }
}
