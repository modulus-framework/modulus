using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Authorization.Audit;
using Modulus.Authorization.EntityFrameworkCore.Audit;
using Modulus.Authorization.Extensions;
using Modulus.Events.Abstractions;
using Modulus.Outbox.Abstractions;
using Xunit;

namespace Modulus.Authorization.EntityFrameworkCore.Tests;

// Exercises durable audit emission end-to-end (auth blueprint §5.14/§16) against
// a real relational database (kept-open in-memory SQLite): EfAuthorizationAuditWriter
// persists rows immediately and durably, AuthorizationAuditRelayProcessor claims,
// dispatches, and marks them processed — or retries with backoff, or dead-letters
// after the retry budget — mirroring Modulus.Outbox's OutboxProcessor guarantees.
[Trait("Category", "Unit")]
public sealed class AuthorizationAuditTests : IDisposable
{
    private sealed class FakeOutboxDispatcher : IOutboxDispatcher
    {
        public List<OutboxMessage> Dispatched { get; } = [];
        public bool ShouldFail { get; set; }

        public Task DispatchAsync(OutboxMessage message, CancellationToken ct)
        {
            if (ShouldFail)
                throw new InvalidOperationException("simulated dispatch failure");

            Dispatched.Add(message);
            return Task.CompletedTask;
        }
    }

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly FakeOutboxDispatcher _dispatcher = new();

    public AuthorizationAuditTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulusAuthorization();
        services.AddEfCoreAuthorizationStores(o => o.UseSqlite(_connection));
        services.AddEfCoreAuthorizationAudit(o =>
        {
            o.MaxRetries = 3;
            o.InitialBackoffSec = 1;
        });
        services.AddSingleton<IOutboxDispatcher>(_dispatcher);

        _provider = services.BuildServiceProvider();
        using var db = _provider
            .GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>()
            .CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private static AuthorizationAdministrativeChangeEvent SampleEvent(string action = "Granted")
        => new("Grant", action, Guid.NewGuid().ToString(), "role:Admin",
            new Dictionary<string, string> { ["permissions"] = "orders:approve" });

    // ── Registration ───────────────────────────────────────────────

    [Fact]
    public void AddEfCoreAuthorizationAudit_supersedes_the_null_default()
    {
        _provider.GetRequiredService<IAuthorizationAuditWriter>()
            .Should().BeOfType<EfAuthorizationAuditWriter>();
    }

    // ── Writer ─────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_persists_a_durable_row()
    {
        var writer = _provider.GetRequiredService<IAuthorizationAuditWriter>();

        await writer.WriteAsync(SampleEvent());

        using var db = _provider
            .GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>()
            .CreateDbContext();
        var rows = await db.Set<OutboxMessage>().ToListAsync();

        rows.Should().ContainSingle();
        rows[0].MessageType.Should().Be("authorization.administrative-change.v1");
        rows[0].ModuleName.Should().Be("Authorization");
        rows[0].ProcessedAt.Should().BeNull();
        rows[0].Payload.Should().Contain("role:Admin").And.Contain("orders:approve");
    }

    [Fact]
    public async Task WriteAsync_registers_the_event_type_so_dispatch_can_resolve_it()
    {
        var writer = _provider.GetRequiredService<IAuthorizationAuditWriter>();
        await writer.WriteAsync(SampleEvent());

        var registry = _provider.GetRequiredService<IIntegrationEventRegistry>();

        registry.TryGetType("authorization.administrative-change.v1", out var type)
            .Should().BeTrue();
        type.Should().Be(typeof(AuthorizationAdministrativeChangeEvent));
    }

    // ── Relay processor ────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_dispatches_and_marks_processed()
    {
        var writer = _provider.GetRequiredService<IAuthorizationAuditWriter>();
        await writer.WriteAsync(SampleEvent());

        var processor = _provider.GetRequiredService<AuthorizationAuditRelayProcessor>();
        await processor.ProcessAsync();

        _dispatcher.Dispatched.Should().ContainSingle();

        using var db = _provider
            .GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>()
            .CreateDbContext();
        var row = await db.Set<OutboxMessage>().SingleAsync();
        row.ProcessedAt.Should().NotBeNull();
        row.LockedBy.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_retries_with_backoff_on_dispatch_failure()
    {
        _dispatcher.ShouldFail = true;
        var writer = _provider.GetRequiredService<IAuthorizationAuditWriter>();
        await writer.WriteAsync(SampleEvent());

        var processor = _provider.GetRequiredService<AuthorizationAuditRelayProcessor>();
        await processor.ProcessAsync();

        using var db = _provider
            .GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>()
            .CreateDbContext();
        var row = await db.Set<OutboxMessage>().SingleAsync();

        row.ProcessedAt.Should().BeNull();
        row.RetryCount.Should().Be(1);
        row.NextAttemptAt.Should().NotBeNull();
        row.Error.Should().Contain("simulated dispatch failure");
    }

    [Fact]
    public async Task ProcessAsync_dead_letters_after_max_retries()
    {
        _dispatcher.ShouldFail = true;
        var writer = _provider.GetRequiredService<IAuthorizationAuditWriter>();
        await writer.WriteAsync(SampleEvent());

        var processor = _provider.GetRequiredService<AuthorizationAuditRelayProcessor>();

        // MaxRetries = 3 (test configuration). Force each attempt's backoff to
        // have already elapsed by re-processing directly — NextAttemptAt is in
        // the future, but we only need RetryCount to reach the budget, so clear
        // it between attempts via a fresh context read.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var db = _provider
                .GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>()
                .CreateDbContext();
            var row = await db.Set<OutboxMessage>().SingleAsync();
            row.NextAttemptAt = null;
            await db.SaveChangesAsync();

            await processor.ProcessAsync();
        }

        using var finalDb = _provider
            .GetRequiredService<IDbContextFactory<AuthorizationStoreDbContext>>()
            .CreateDbContext();
        var finalRow = await finalDb.Set<OutboxMessage>().SingleAsync();

        finalRow.ProcessedAt.Should().BeNull();
        finalRow.RetryCount.Should().Be(3);

        // Below the retry budget check (RetryCount < MaxRetries) excludes it
        // from future candidate selection — verify one more pass is a no-op.
        finalRow.NextAttemptAt = null;
        await finalDb.SaveChangesAsync();
        _dispatcher.Dispatched.Clear();
        await processor.ProcessAsync();
        _dispatcher.Dispatched.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessAsync_is_a_noop_when_no_rows_are_pending()
    {
        var processor = _provider.GetRequiredService<AuthorizationAuditRelayProcessor>();

        var act = async () => await processor.ProcessAsync();

        await act.Should().NotThrowAsync();
        _dispatcher.Dispatched.Should().BeEmpty();
    }
}
