namespace Modulus.AuditLogging.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.AuditLogging.Security;
using Modulus.AuditLogging.Security.Retention;
using Modulus.Core.Abstractions;
using Xunit;

/// <summary>A11-2: retention archives the entries it purges first, the archive is append-only and verifies on its own, and a conflicting archive is refused.</summary>
[Trait("Category", "Unit")]
public sealed class EfSecurityAuditArchiveTests : IAsyncLifetime
{
    private static readonly Guid Company = Guid.Parse("a1000000-0000-0000-0000-00000000000a");
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

    private readonly string _file = Path.Combine(Path.GetTempPath(), $"modulus-archive-{Guid.NewGuid():N}.db");
    private readonly List<ServiceProvider> _providers = [];

    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = _file, DefaultTimeout = 30 }.ToString();

    public async Task InitializeAsync()
    {
        await using var scope = Node().CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EfSecurityAuditStoreTests.AuditDb>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var provider in _providers)
            await provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(_file);
    }

    private ServiceProvider Node()
    {
        var services = new ServiceCollection();
        services.AddDbContext<EfSecurityAuditStoreTests.AuditDb>(o => o.UseSqlite(ConnectionString));
        services.AddModulusAuditStore<EfSecurityAuditStoreTests.AuditDb>();
        services.AddModulusAuditPurgeRole(o => o.UseSqlite(ConnectionString));
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private static SecurityAuditEvent Event(string action, DateTimeOffset at) => new()
    {
        Category = SecurityAuditCategories.Tenancy,
        Action = action,
        TenantId = Company,
        OccurredAt = at,
        Details = new Dictionary<string, string?> { ["n"] = action },
    };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static async Task<List<SecurityAuditRecord>> ReadAllAsync(ISecurityAuditStore store)
    {
        var records = new List<SecurityAuditRecord>();
        await foreach (var record in store.ReadAsync(Company))
            records.Add(record);
        return records;
    }

    private static async Task SeedAsync(ISecurityAuditStore store)
    {
        // Three entries older than the retention period, then one recent entry.
        for (var i = 1; i <= 3; i++)
            await store.AppendAsync(Event($"old{i}", Now.AddDays(-30 + i)));
        await store.AppendAsync(Event("recent", Now.AddHours(-1)));
    }

    [Fact]
    public async Task Retention_archives_the_purged_entries_first_and_the_archive_verifies_on_its_own()
    {
        var node = Node();
        var store = node.GetRequiredService<ISecurityAuditStore>();
        await SeedAsync(store);
        var retention = new SecurityAuditRetention(
            store,
            node.GetRequiredService<EfSecurityAuditArchive>(),
            node.GetRequiredService<ISecurityAuditHolds>(),
            new FixedClock(Now),
            Options.Create(new SecurityAuditRetentionOptions { Enabled = true, Retention = TimeSpan.FromDays(1) }));

        var results = await retention.RunAsync();

        results.Single().Outcome.Should().Be(SecurityAuditRetentionOutcome.Purged);
        results.Single().Purged.Should().Be(3);
        var archive = node.GetRequiredService<EfSecurityAuditArchive>();
        (await archive.ReadAsync(Company)).Select(r => r.Sequence).Should().Equal(1, 2, 3);
        (await archive.VerifyAsync(Company)).IsValid.Should().BeTrue();
        (await ReadAllAsync(store)).Select(r => r.Sequence).Should().Equal(new long[] { 4 }, "the recent entry stays live");
    }

    [Fact]
    public async Task Archiving_the_same_entries_again_changes_nothing()
    {
        var node = Node();
        var store = node.GetRequiredService<ISecurityAuditStore>();
        await SeedAsync(store);
        var records = await ReadAllAsync(store);
        var archive = node.GetRequiredService<EfSecurityAuditArchive>();
        await archive.ArchiveAsync(Company, records.Take(2).ToList());

        await archive.ArchiveAsync(Company, records.Take(3).ToList());

        (await archive.ReadAsync(Company)).Should().HaveCount(3, "the first two were already archived and are not written twice");
    }

    [Fact]
    public async Task An_archive_that_disagrees_with_a_stored_entry_is_refused()
    {
        var node = Node();
        var store = node.GetRequiredService<ISecurityAuditStore>();
        await SeedAsync(store);
        var records = await ReadAllAsync(store);
        var archive = node.GetRequiredService<EfSecurityAuditArchive>();
        await archive.ArchiveAsync(Company, records.Take(3).ToList());

        // A record with the same sequence but another hash cannot replace the archived one.
        var forged = records[0] with { Action = "forged", Hash = new string('f', 64) };
        var act = () => archive.ArchiveAsync(Company, [forged, records[1], records[2]]);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await archive.ReadAsync(Company)).Single(r => r.Sequence == 1).Action.Should().Be("old1");
    }

    [Fact]
    public async Task A_segment_that_does_not_verify_is_not_archived()
    {
        var node = Node();
        var store = node.GetRequiredService<ISecurityAuditStore>();
        await SeedAsync(store);
        var records = await ReadAllAsync(store);
        var archive = node.GetRequiredService<EfSecurityAuditArchive>();

        // The first entry no longer links to the chain's history: the segment is broken before anything is written.
        var broken = records[0] with { PreviousHash = new string('a', 64) };
        var act = () => archive.ArchiveAsync(Company, [broken, records[1], records[2]]);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await archive.ReadAsync(Company)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_segment_starting_at_the_first_entry_must_follow_the_genesis_hash()
    {
        var node = Node();
        var store = node.GetRequiredService<ISecurityAuditStore>();
        await SeedAsync(store);
        var records = await ReadAllAsync(store);
        var archive = node.GetRequiredService<EfSecurityAuditArchive>();

        // Its own link is consistent with a made-up predecessor, but the chain's first entry must follow the genesis hash.
        var fabricated = records[0] with { PreviousHash = new string('a', 64) };
        var act = () => archive.ArchiveAsync(Company, [fabricated]);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await archive.ReadAsync(Company)).Should().BeEmpty();
    }
}
