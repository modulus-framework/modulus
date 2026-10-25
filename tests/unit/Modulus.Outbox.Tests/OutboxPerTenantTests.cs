using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.EntityFrameworkCore.Isolation;
using Modulus.Outbox;
using Modulus.Outbox.Abstractions;
using Xunit;

namespace Modulus.Outbox.Tests;

/// <summary>
/// A context whose database is kept per tenant has no host database to drain. The processor visits every active tenant
/// and drains that tenant's own outbox table.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OutboxPerTenantTests : IAsyncDisposable
{
    private static readonly Guid TenantA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid TenantC = Guid.Parse("c0000000-0000-0000-0000-00000000000c");

    private readonly string _run = Guid.NewGuid().ToString("N");
    private readonly List<SqliteConnection> _open = [];
    private readonly Dictionary<Guid, SqliteConnection> _stores = new();
    private readonly OutboxProcessorTests.FakeDispatcher _dispatcher = new(null);
    private readonly ScopedTenant _tenant = new();

    private async Task<SqliteConnection> OpenStoreAsync(Guid? tenantId)
    {
        var key = tenantId ?? Guid.Empty;
        if (_stores.TryGetValue(key, out var existing))
            return existing;

        // Kept alive for the test: an in-memory SQLite database lives as long as its last open connection.
        var name = tenantId is { } id ? id.ToString("N") : "host";
        var conn = new SqliteConnection($"DataSource=outbox-{_run}-{name};Mode=Memory;Cache=Shared");
        await conn.OpenAsync();
        _open.Add(conn);
        await using (var db = new OutboxProcessorTests.TestOutboxDbContext(OptionsFor(conn)))
            await db.Database.EnsureCreatedAsync();
        _stores[key] = conn;
        return conn;
    }

    private static DbContextOptions<OutboxProcessorTests.TestOutboxDbContext> OptionsFor(SqliteConnection conn)
        => new DbContextOptionsBuilder<OutboxProcessorTests.TestOutboxDbContext>().UseSqlite(conn).Options;

    private async Task<ServiceProvider> BuildAsync(IEnumerable<Guid> activeTenants)
    {
        // Every store exists before the processor runs, so the factory below can pick one synchronously.
        await OpenStoreAsync(null);
        foreach (var id in new[] { TenantA, TenantB, TenantC })
            await OpenStoreAsync(id);

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddSingleton<IOutboxDispatcher>(_dispatcher);
        services.AddSingleton(new TenantDatabaseRoute(typeof(OutboxProcessorTests.TestOutboxDbContext), "primary", "sqlite"));
        services.AddSingleton<ITenantDirectory>(new FakeDirectory(activeTenants));
        services.AddSingleton<ITenantContextRestorer>(new FakeRestorer([TenantA, TenantB]));
        services.AddScoped(sp =>
        {
            var tenant = sp.GetRequiredService<ICurrentTenant>().TenantId;
            return new OutboxProcessorTests.TestOutboxDbContext(OptionsFor(_stores[tenant ?? Guid.Empty]));
        });
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<OutboxProcessorTests.TestOutboxDbContext>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ProcessAsync_PerTenantContext_DispatchesEachTenantsRows_AndNotTheHost()
    {
        var sp = await BuildAsync([TenantA, TenantB]);
        await SeedAsync(TenantA);
        await SeedAsync(TenantB);
        var processor = new OutboxProcessor(sp, Options.Create(new OutboxOptions()), NullLogger<OutboxProcessor>.Instance);

        await processor.ProcessAsync();

        _dispatcher.Calls.Should().Be(2, "one row per tenant is dispatched, and the empty host database adds none");
        (await ProcessedCountAsync(TenantA)).Should().Be(1);
        (await ProcessedCountAsync(TenantB)).Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_OneTenantCannotBeVerified_TheOthersStillDrain()
    {
        // TenantC is listed as active but the restorer rejects it: that tenant's pass fails, the others still run.
        var sp = await BuildAsync([TenantA, TenantC, TenantB]);
        await SeedAsync(TenantA);
        await SeedAsync(TenantB);
        var processor = new OutboxProcessor(sp, Options.Create(new OutboxOptions()), NullLogger<OutboxProcessor>.Instance);

        await processor.ProcessAsync();

        _dispatcher.Calls.Should().Be(2);
        (await ProcessedCountAsync(TenantB)).Should().Be(1);
    }

    private async Task SeedAsync(Guid tenantId)
    {
        await using var db = new OutboxProcessorTests.TestOutboxDbContext(OptionsFor(_stores[tenantId]));
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            TenantId = tenantId,
            MessageType = "Modulus.Outbox.Tests.TestEvent, Modulus.Outbox.Tests",
            Payload = "{}",
            ModuleName = "Test",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> ProcessedCountAsync(Guid tenantId)
    {
        await using var db = new OutboxProcessorTests.TestOutboxDbContext(OptionsFor(_stores[tenantId]));
        return await db.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt != null);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var conn in _open)
            conn.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class ScopedTenant : ICurrentTenant
    {
        private readonly AsyncLocal<TenantInfo?> _current = new();

        public Guid? TenantId => _current.Value?.TenantId;
        public string? TenantSlug => _current.Value?.TenantSlug;
        public bool IsAvailable => TenantId is not null;
        public bool IsHost => false;

        public IDisposable Change(TenantInfo? tenant)
        {
            var previous = _current.Value;
            _current.Value = tenant;
            return new Restore(this, previous);
        }

        private sealed class Restore(ScopedTenant owner, TenantInfo? previous) : IDisposable
        {
            public void Dispose() => owner._current.Value = previous;
        }
    }

    private sealed class FakeDirectory(IEnumerable<Guid> ids) : ITenantDirectory
    {
        private readonly IReadOnlyList<Guid> _ids = [.. ids];

        public Task<IReadOnlyList<Guid>> ListActiveTenantIdsAsync(CancellationToken ct = default) => Task.FromResult(_ids);
    }

    private sealed class FakeRestorer(IEnumerable<Guid> known) : ITenantContextRestorer
    {
        private readonly HashSet<Guid> _known = [.. known];

        public ValueTask<TenantInfo> VerifyAsync(Guid tenantId, CancellationToken ct = default)
            => _known.Contains(tenantId)
                ? ValueTask.FromResult(new TenantInfo(tenantId, tenantId.ToString("N")))
                : throw new TenantContextRejectedException(tenantId);
    }
}
