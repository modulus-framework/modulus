using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.MultiTenancy;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using Xunit;

namespace Modulus.MultiTenancy.Tests;

/// <summary>
/// TEST-3: a membership changed on one node is decided by another node at its next check. The membership store queries the database on
/// every check and caches nothing, so the measured staleness is zero. Each node is its own container over one SQLite file.
/// </summary>
[Trait("Category", "Unit")]
public sealed class MembershipAcrossNodesTests : IDisposable
{
    private static readonly Guid s_user = Guid.Parse("e0000000-0000-0000-0000-000000000001");

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"modulus-membership-nodes-{Guid.NewGuid():N}.db");
    private readonly List<ServiceProvider> _nodes = [];

    public void Dispose()
    {
        foreach (var node in _nodes)
            node.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private ServiceProvider Node()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMultiTenancy(t => t.UseHeaderResolver());
        services.AddEfCoreTenantStore(o => o.UseSqlite($"Data Source={_databasePath}"));
        var provider = services.BuildServiceProvider();
        _nodes.Add(provider);
        return provider;
    }

    // Two replicas over one database: the writer creates the schema and the tenant, the reader is a second container.
    private async Task<(ServiceProvider Writer, ServiceProvider Reader, Guid TenantId)> TwoNodesAsync()
    {
        var writer = Node();
        using (var scope = writer.CreateScope())
            scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>().Database.EnsureCreated();

        var tenant = await WithManager(writer, m => m.CreateAsync("acme", "Acme Inc"));
        return (writer, Node(), tenant.TenantId);
    }

    private static async Task<T> WithManager<T>(ServiceProvider node, Func<TenantManager, Task<T>> act)
    {
        using var scope = node.CreateScope();
        return await act(scope.ServiceProvider.GetRequiredService<TenantManager>());
    }

    private static async Task<bool> IsMemberAsync(ServiceProvider node, Guid tenantId)
    {
        using var scope = node.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>().IsMemberAsync(s_user, tenantId);
    }

    [Fact]
    public async Task A_membership_added_on_one_node_is_honoured_by_the_other_at_once()
    {
        var (writer, reader, tenantId) = await TwoNodesAsync();
        (await IsMemberAsync(reader, tenantId)).Should().BeFalse();

        (await WithManager(writer, m => m.AddMemberAsync(s_user, tenantId))).Should().BeTrue();

        (await IsMemberAsync(reader, tenantId)).Should().BeTrue();
    }

    [Fact]
    public async Task A_membership_removed_on_one_node_is_refused_by_the_other_at_once()
    {
        var (writer, reader, tenantId) = await TwoNodesAsync();
        await WithManager(writer, m => m.AddMemberAsync(s_user, tenantId));
        (await IsMemberAsync(reader, tenantId)).Should().BeTrue();

        (await WithManager(writer, m => m.RemoveMemberAsync(s_user, tenantId))).Should().BeTrue();

        (await IsMemberAsync(reader, tenantId)).Should().BeFalse("the other node's next check reads the removed membership");
    }

    [Fact]
    public async Task Concurrent_adds_on_two_nodes_never_exceed_the_seat_limit()
    {
        var (writer, reader, tenantId) = await TwoNodesAsync();
        await WithManager(writer, m => m.SetMaxUsersAsync(tenantId, 1));

        // Eight people try for the last seat at once, from both nodes.
        var attempts = Enumerable.Range(0, 8).Select(i => Guid.NewGuid()).ToArray();
        var outcomes = await Task.WhenAll(attempts.Select((user, i) => TryAddAsync(i % 2 == 0 ? writer : reader, user, tenantId)));

        var seated = outcomes.Count(ok => ok);
        seated.Should().Be(1, "the company has one seat: exactly one attempt wins it, and the rest are refused");
    }

    private static async Task<bool> TryAddAsync(ServiceProvider node, Guid user, Guid tenantId)
    {
        try
        {
            using var scope = node.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<TenantManager>().AddMemberAsync(user, tenantId);
        }
        catch (Modulus.Core.Abstractions.Exceptions.ConflictException)
        {
            return false;
        }
    }
}
