using FluentAssertions;
using Microsoft.Extensions.Options;
using Modulus.AuditLogging.Security;
using Modulus.AuditLogging.Security.Retention;
using Modulus.Core.Abstractions;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>A11-2: restore validation proves, read-only, that the archive and the live chain still join into one chain from genesis.</summary>
[Trait("Category", "Unit")]
public sealed class SecurityAuditRestoreValidatorTests
{
    private static readonly Guid s_chain = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset s_now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Keeps what retention archives and reads it back, as a real archive does.
    private sealed class MemoryArchive : ISecurityAuditArchive
    {
        public List<SecurityAuditRecord> Rows { get; } = [];

        public Task ArchiveAsync(Guid chainId, IReadOnlyList<SecurityAuditRecord> records, CancellationToken ct = default)
        {
            Rows.AddRange(records);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SecurityAuditRecord>> ReadAsync(Guid chainId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityAuditRecord>>(Rows.Where(r => r.ChainId == chainId).OrderBy(r => r.Sequence).ToList());
    }

    // An archive that accepts writes but cannot be read back.
    private sealed class WriteOnlyArchive : ISecurityAuditArchive
    {
        public Task ArchiveAsync(Guid chainId, IReadOnlyList<SecurityAuditRecord> records, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static SecurityAuditEvent Event(DateTimeOffset at, string name) => new()
    {
        Category = SecurityAuditCategories.Identity,
        Action = "signin.success",
        TenantId = s_chain,
        Actor = name,
        OccurredAt = at,
        Details = new Dictionary<string, string?>(),
    };

    private static async Task<InMemorySecurityAuditStore> StoreWithAsync(int oldEntries, int recentEntries)
    {
        var store = new InMemorySecurityAuditStore(TimeProvider.System);
        for (var i = 1; i <= oldEntries; i++)
            await store.AppendAsync(Event(s_now.AddDays(-1000 + i), $"old{i}"));
        for (var i = 1; i <= recentEntries; i++)
            await store.AppendAsync(Event(s_now.AddDays(-10 + i), $"recent{i}"));
        return store;
    }

    private static async Task PurgeOldEntriesAsync(InMemorySecurityAuditStore store, MemoryArchive archive)
    {
        var retention = new SecurityAuditRetention(
            store, archive, new SecurityAuditHolds(), new FixedClock(s_now),
            Options.Create(new SecurityAuditRetentionOptions { Retention = TimeSpan.FromDays(730) }));
        await retention.RunAsync();
    }

    [Fact]
    public async Task A_purged_chain_whose_archive_joins_the_live_store_is_valid()
    {
        var store = await StoreWithAsync(oldEntries: 3, recentEntries: 1);
        var archive = new MemoryArchive();
        await PurgeOldEntriesAsync(store, archive);

        var result = await new SecurityAuditRestoreValidator(store, archive).ValidateAsync(s_chain);

        result.IsValid.Should().BeTrue();
        result.ArchivedEntries.Should().Be(3);
        result.LiveEntries.Should().Be(1);
        result.Problem.Should().BeNull();
    }

    [Fact]
    public async Task A_chain_that_was_never_purged_is_valid_from_genesis_and_needs_no_archive()
    {
        var store = await StoreWithAsync(oldEntries: 0, recentEntries: 4);

        var result = await new SecurityAuditRestoreValidator(store, new WriteOnlyArchive()).ValidateAsync(s_chain);

        result.IsValid.Should().BeTrue();
        result.ArchivedEntries.Should().Be(0);
        result.LiveEntries.Should().Be(4);
    }

    [Fact]
    public async Task An_archive_that_lost_its_last_purged_entry_does_not_join_the_checkpoint()
    {
        var store = await StoreWithAsync(oldEntries: 3, recentEntries: 1);
        var archive = new MemoryArchive();
        await PurgeOldEntriesAsync(store, archive);
        archive.Rows.RemoveAt(2);

        var result = await new SecurityAuditRestoreValidator(store, archive).ValidateAsync(s_chain);

        result.IsValid.Should().BeFalse();
        result.Problem.Should().Contain("does not end at the checkpoint");
    }

    [Fact]
    public async Task An_archived_entry_that_was_edited_fails_verification()
    {
        var store = await StoreWithAsync(oldEntries: 3, recentEntries: 1);
        var archive = new MemoryArchive();
        await PurgeOldEntriesAsync(store, archive);
        archive.Rows[0] = archive.Rows[0] with { Action = "forged" };

        var result = await new SecurityAuditRestoreValidator(store, archive).ValidateAsync(s_chain);

        result.IsValid.Should().BeFalse();
        result.Problem.Should().Contain("does not verify");
    }

    [Fact]
    public async Task An_archive_that_cannot_be_read_back_leaves_the_chain_unprovable()
    {
        var store = await StoreWithAsync(oldEntries: 3, recentEntries: 1);
        var archive = new MemoryArchive();
        await PurgeOldEntriesAsync(store, archive);

        var result = await new SecurityAuditRestoreValidator(store, new WriteOnlyArchive()).ValidateAsync(s_chain);

        result.IsValid.Should().BeFalse();
        result.Problem.Should().Contain("cannot be read back");
    }

    [Fact]
    public async Task Archived_entries_with_no_checkpoint_in_the_store_are_refused()
    {
        var store = await StoreWithAsync(oldEntries: 0, recentEntries: 2);
        var archive = new MemoryArchive();
        SecurityAuditRecord? first = null;
        await foreach (var record in store.ReadAsync(s_chain))
            first ??= record;
        archive.Rows.Add(first!);

        var result = await new SecurityAuditRestoreValidator(store, archive).ValidateAsync(s_chain);

        result.IsValid.Should().BeFalse();
        result.Problem.Should().Contain("no checkpoint");
    }
}
