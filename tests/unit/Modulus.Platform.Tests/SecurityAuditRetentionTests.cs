using FluentAssertions;
using Microsoft.Extensions.Options;
using Modulus.AuditLogging.Security;
using Modulus.AuditLogging.Security.Retention;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>A11-1: retention purges only an archived, expired prefix of a chain; the rest verifies from the checkpoint, and holds and missing archives stop it.</summary>
[Trait("Category", "Unit")]
public sealed class SecurityAuditRetentionTests
{
    private static readonly Guid s_company = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset s_now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

    private sealed class RecordingArchive : ISecurityAuditArchive
    {
        public List<SecurityAuditRecord> Archived { get; } = [];

        public Task ArchiveAsync(Guid chainId, IReadOnlyList<SecurityAuditRecord> records, CancellationToken ct = default)
        {
            Archived.AddRange(records);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static SecurityAuditEvent Event(DateTimeOffset at, string name) => new()
    {
        Category = SecurityAuditCategories.Identity,
        Action = "signin.success",
        TenantId = s_company,
        Actor = name,
        OccurredAt = at,
        Details = new Dictionary<string, string?>(),
    };

    private static async Task<InMemorySecurityAuditStore> StoreAsync(params DateTimeOffset[] times)
    {
        var store = new InMemorySecurityAuditStore(TimeProvider.System);
        for (var i = 0; i < times.Length; i++)
            await store.AppendAsync(Event(times[i], $"e{i + 1}"));
        return store;
    }

    private static SecurityAuditRetention Retention(
        ISecurityAuditStore store, ISecurityAuditArchive archive, ISecurityAuditHolds? holds = null, bool enabled = true)
        => new(store, archive, holds ?? new SecurityAuditHolds(), new FixedClock(s_now),
            Options.Create(new SecurityAuditRetentionOptions { Retention = TimeSpan.FromDays(730), Enabled = enabled }));

    private static readonly DateTimeOffset s_old = s_now.AddDays(-1000);
    private static readonly DateTimeOffset s_recent = s_now.AddDays(-10);

    [Fact]
    public async Task Expired_entries_are_archived_and_purged_and_the_rest_verifies_from_the_checkpoint()
    {
        var store = await StoreAsync(s_old, s_old, s_recent, s_recent);
        var archive = new RecordingArchive();

        var results = await Retention(store, archive).RunAsync();

        results.Should().ContainSingle().Which.Should().Be(new SecurityAuditRetentionResult(s_company, SecurityAuditRetentionOutcome.Purged, 2));
        archive.Archived.Select(r => r.Sequence).Should().Equal(new long[] { 1, 2 }, "the archive keeps the purged entries in order");

        var checkpoint = await store.GetCheckpointAsync(s_company);
        checkpoint.Should().NotBeNull();
        checkpoint!.Sequence.Should().Be(2);

        var remaining = new List<SecurityAuditRecord>();
        await foreach (var record in store.ReadAsync(s_company))
            remaining.Add(record);
        remaining.Select(r => r.Sequence).Should().Equal(new long[] { 3, 4 }, "the recent entries are kept");
        SecurityAuditChain.VerifyFrom(remaining, checkpoint).IsValid.Should().BeTrue();
        (await store.VerifyChainAsync(s_company)).IsValid.Should().BeFalse("a full verify no longer starts at the genesis entry");
    }

    [Fact]
    public async Task A_checkpoint_anchor_still_verifies_and_an_older_anchor_must_be_refreshed()
    {
        var store = await StoreAsync(s_old, s_old, s_recent);
        var before = (await store.GetHeadsAsync()).Single();
        await Retention(store, new RecordingArchive()).RunAsync();
        var checkpoint = (await store.GetCheckpointAsync(s_company))!;

        var remaining = new List<SecurityAuditRecord>();
        await foreach (var record in store.ReadAsync(s_company))
            remaining.Add(record);

        SecurityAuditChain.VerifyFrom(remaining, checkpoint, checkpoint).IsValid.Should().BeTrue("the checkpoint is its own anchor");
        var stale = SecurityAuditChain.VerifyFrom(remaining, checkpoint, new SecurityAuditHead(s_company, 1, before.Hash));
        stale.IsValid.Should().BeFalse("an anchor on a purged entry cannot be checked; it must be re-anchored");
    }

    [Fact]
    public async Task A_checkpoint_with_the_wrong_hash_fails_verification()
    {
        var store = await StoreAsync(s_old, s_recent);
        await Retention(store, new RecordingArchive()).RunAsync();
        var remaining = new List<SecurityAuditRecord>();
        await foreach (var record in store.ReadAsync(s_company))
            remaining.Add(record);

        SecurityAuditChain.VerifyFrom(remaining, new SecurityAuditHead(s_company, 1, new string('f', 64))).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_chain_on_legal_hold_is_never_purged()
    {
        var store = await StoreAsync(s_old, s_old);
        var holds = new SecurityAuditHolds();
        await holds.PlaceAsync(s_company, "litigation 2026-17");
        var archive = new RecordingArchive();

        var results = await Retention(store, archive, holds).RunAsync();

        results.Single().Outcome.Should().Be(SecurityAuditRetentionOutcome.Held);
        archive.Archived.Should().BeEmpty();
        (await store.VerifyChainAsync(s_company)).IsValid.Should().BeTrue("nothing was removed");

        (await holds.ReleaseAsync(s_company)).Should().BeTrue();
        (await Retention(store, archive, holds).RunAsync()).Single().Outcome.Should().Be(SecurityAuditRetentionOutcome.Purged);
    }

    [Fact]
    public async Task Without_an_archive_nothing_is_purged()
    {
        var store = await StoreAsync(s_old, s_recent);

        var results = await Retention(store, new NoSecurityAuditArchive()).RunAsync();

        results.Single().Outcome.Should().Be(SecurityAuditRetentionOutcome.RefusedNoArchive);
        (await store.VerifyChainAsync(s_company)).IsValid.Should().BeTrue("every entry is still there");
        (await store.GetCheckpointAsync(s_company)).Should().BeNull();
    }

    [Fact]
    public async Task A_chain_with_nothing_past_the_retention_period_is_left_alone()
    {
        var store = await StoreAsync(s_recent, s_recent);

        (await Retention(store, new RecordingArchive()).RunAsync()).Single().Outcome.Should().Be(SecurityAuditRetentionOutcome.NothingToPurge);
    }

    // A store that keeps every entry: it does not override the purge, so the default refuses.
    private sealed class KeepEverythingStore(ISecurityAuditStore inner) : ISecurityAuditStore
    {
        public Task<SecurityAuditRecord> AppendAsync(SecurityAuditEvent auditEvent, CancellationToken ct = default) => inner.AppendAsync(auditEvent, ct);

        public IAsyncEnumerable<SecurityAuditRecord> ReadAsync(Guid chainId, long fromSequence = 1, CancellationToken ct = default)
            => inner.ReadAsync(chainId, fromSequence, ct);

        public Task<IReadOnlyList<SecurityAuditHead>> GetHeadsAsync(CancellationToken ct = default) => inner.GetHeadsAsync(ct);
    }

    [Fact]
    public async Task A_store_that_cannot_purge_reports_it_and_keeps_the_chain()
    {
        var inner = await StoreAsync(s_old, s_recent);
        var store = new KeepEverythingStore(inner);

        (await Retention(store, new RecordingArchive()).RunAsync()).Single().Outcome.Should().Be(SecurityAuditRetentionOutcome.RefusedStore);
        (await inner.VerifyChainAsync(s_company)).IsValid.Should().BeTrue("the chain is unchanged");
    }

    [Fact]
    public async Task Disabled_retention_does_nothing()
    {
        var store = await StoreAsync(s_old);

        (await Retention(store, new RecordingArchive(), enabled: false).RunAsync()).Should().BeEmpty();
    }
}
