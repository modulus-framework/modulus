using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Identity.Abstractions;
using Modulus.Identity.EntityFrameworkCore;
using Xunit;

namespace Modulus.Identity.Tests;

/// <summary>
/// TEST-3: a session ended on one node is ended on another at its next check. The registry reads the session row on every check and
/// nothing is cached, so the measured staleness is zero. Each node is its own database context over one SQLite file.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SessionsAcrossNodesTests : IDisposable
{
    private static readonly Guid s_user = Guid.Parse("d0000000-0000-0000-0000-000000000001");

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"modulus-sessions-nodes-{Guid.NewGuid():N}.db");
    private readonly List<ModulusIdentityDbContext> _contexts = [];
    private readonly ManualClock _clock = new();

    private sealed class ManualClock : TimeProvider
    {
        private readonly DateTimeOffset _now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;
    }

    public SessionsAcrossNodesTests()
    {
        using var first = Node();
        first.Database.EnsureCreated();
        first.Users.Add(new ModulusUser { Id = s_user, UserName = "planner", NormalizedUserName = "PLANNER", Email = "planner@example.test", NormalizedEmail = "PLANNER@EXAMPLE.TEST" });
        first.SaveChanges();
    }

    public void Dispose()
    {
        foreach (var context in _contexts)
            context.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private ModulusIdentityDbContext Node()
    {
        var options = new DbContextOptionsBuilder().UseSqlite($"Data Source={_databasePath}").Options;
        var context = new ModulusIdentityDbContext(options, new NullCurrentTenant());
        _contexts.Add(context);
        return context;
    }

    private SessionRegistry<ModulusIdentityDbContext> Registry(ModulusIdentityDbContext node)
        => new(node, Options.Create(new SessionOptions { Enabled = true }), _clock);

    private static SessionStart Start() => new(s_user, null, "spa", ["Planner"], ["pwd"], null, null, null);

    [Fact]
    public async Task A_session_started_on_one_node_is_active_on_the_other_at_once()
    {
        var a = Registry(Node());
        var b = Registry(Node());

        var started = await a.StartAsync(Start());

        (await b.TouchAsync(started.SessionId!.Value)).Should().Be(SessionState.Active);
    }

    [Fact]
    public async Task Ending_a_session_on_one_node_ends_it_on_the_other_at_once()
    {
        var a = Registry(Node());
        var b = Registry(Node());
        var started = await a.StartAsync(Start());
        (await b.TouchAsync(started.SessionId!.Value)).Should().Be(SessionState.Active);

        (await a.EndAsync(s_user, started.SessionId!.Value, "signed out")).Should().BeTrue();

        (await b.TouchAsync(started.SessionId!.Value)).Should().Be(SessionState.Ended, "the other node's next check reads the ended row");
    }

    [Fact]
    public async Task Ending_all_sessions_on_one_node_ends_them_all_on_the_other_at_once()
    {
        var a = Registry(Node());
        var b = Registry(Node());
        var first = await a.StartAsync(Start());
        var second = await a.StartAsync(Start());

        (await a.EndAllAsync(s_user, "revoked")).Should().Be(2);

        (await b.TouchAsync(first.SessionId!.Value)).Should().Be(SessionState.Ended);
        (await b.TouchAsync(second.SessionId!.Value)).Should().Be(SessionState.Ended);
    }
}
