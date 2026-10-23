using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.EntityFrameworkCore;
using Modulus.Authorization.Extensions;
using Modulus.Authorization.Grants;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

/// <summary>
/// TEST-3: a grant change made on one node is decided by another node at once. The grant store is read on every decision with no cache,
/// so the measured staleness is zero: a grant, a revocation and a deny each show on the other replica in the next request.
/// </summary>
[Trait("Category", "Unit")]
public sealed class GrantChangesAcrossNodesTests : IDisposable
{
    private const string Read = "orders:read";
    private const string Role = "Sales";

    private static readonly Guid s_user = Guid.Parse("c0000000-0000-0000-0000-000000000001");

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"modulus-grants-nodes-{Guid.NewGuid():N}.db");
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
        services.AddSingleton<ICurrentTenant, Modulus.Core.Null.NullCurrentTenant>();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite($"Data Source={_databasePath}"));
        var provider = services.BuildServiceProvider();
        _nodes.Add(provider);
        return provider;
    }

    // Two replicas over one database: the writer creates the schema, the reader is a second container.
    private (EfPermissionGrantStore Writer, ServiceProvider Reader) TwoNodes()
    {
        var writerNode = Node();
        using (var db = writerNode.GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>().CreateDbContext())
            db.Database.EnsureCreated();

        return (writerNode.GetRequiredService<EfPermissionGrantStore>(), Node());
    }

    // A decision on a node: its own request scope, as a request would have. The grant store memoizes per scope, so a long-lived
    // scope would hide changes made after it first read; a fresh scope per decision is what the measurement is about.
    private static IReadOnlySet<string> Decide(ServiceProvider node, PrincipalGrantQuery principal)
    {
        using var scope = node.CreateScope();
        var registry = new PermissionRegistry();
        registry.Add(Read, "Read orders.", null);
        registry.Freeze();
        return new PermissionResolver(scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>(), registry).Resolve(principal);
    }

    private static bool Allows(ServiceProvider node) => Decide(node, new PrincipalGrantQuery(s_user, [])).Contains(Read);

    [Fact]
    public async Task Within_one_scope_a_change_made_after_the_first_read_is_not_seen()
    {
        // The grant store memoizes per scope (one request). A scope that outlives a change keeps its first answer until it ends.
        var (writer, reader) = TwoNodes();
        using var scope = reader.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPermissionGrantStore>();
        var registry = new PermissionRegistry();
        registry.Add(Read, "Read orders.", null);
        registry.Freeze();
        var resolver = new PermissionResolver(store, registry);
        resolver.Resolve(new PrincipalGrantQuery(s_user, [])).Should().BeEmpty();

        await writer.GrantToUserAsync(s_user, [Read]);

        resolver.Resolve(new PrincipalGrantQuery(s_user, [])).Should().BeEmpty("the scope's memoized read is served until the scope ends");
        Allows(reader).Should().BeTrue("a fresh scope on the other node reads the grant at once");
    }

    [Fact]
    public async Task A_grant_made_on_one_node_is_honoured_by_the_other_at_once()
    {
        var (writer, reader) = TwoNodes();
        Allows(reader).Should().BeFalse("nothing is granted yet");

        await writer.GrantToUserAsync(s_user, [Read]);

        Allows(reader).Should().BeTrue();
    }

    [Fact]
    public async Task A_revocation_on_one_node_stops_the_other_at_once()
    {
        var (writer, reader) = TwoNodes();
        await writer.GrantToUserAsync(s_user, [Read]);
        Allows(reader).Should().BeTrue();

        await writer.RevokeFromUserAsync(s_user, Read);

        Allows(reader).Should().BeFalse("the revocation is the next decision on the other node");
    }

    [Fact]
    public async Task A_deny_on_one_node_overrides_a_role_grant_on_the_other_at_once()
    {
        var (writer, reader) = TwoNodes();
        await writer.GrantToRoleAsync(Role, [Read]);
        var principal = new PrincipalGrantQuery(s_user, [Role]);
        Decide(reader, principal).Should().Contain(Read);

        await writer.DenyToUserAsync(s_user, [Read]);

        Decide(reader, principal).Should().NotContain(Read, "a deny on the user wins over the role grant, whichever node decides");
    }
}
