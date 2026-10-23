using Microsoft.Extensions.Options;

namespace Modulus.AuditLogging.Security.Retention;

/// <summary>
/// Archives and purges the oldest security audit entries of each chain (A11-1). Only a contiguous prefix older than
/// <see cref="SecurityAuditRetentionOptions.Retention"/> is purged; the remaining chain verifies from the checkpoint the purge leaves.
/// A chain is left alone when it is on hold, when it fails verification, or when no archive is configured.
/// </summary>
public sealed class SecurityAuditRetention(
    ISecurityAuditStore store,
    ISecurityAuditArchive archive,
    ISecurityAuditHolds holds,
    TimeProvider clock,
    IOptions<SecurityAuditRetentionOptions> options)
{
    /// <summary>Runs retention over every chain once.</summary>
    public async Task<IReadOnlyList<SecurityAuditRetentionResult>> RunAsync(CancellationToken ct = default)
    {
        var results = new List<SecurityAuditRetentionResult>();
        if (!options.Value.Enabled)
            return results;

        foreach (var head in await store.GetHeadsAsync(ct).ConfigureAwait(false))
            results.Add(await PurgeChainAsync(head.ChainId, ct).ConfigureAwait(false));
        return results;
    }

    private async Task<SecurityAuditRetentionResult> PurgeChainAsync(Guid chainId, CancellationToken ct)
    {
        if (await holds.IsHeldAsync(chainId, ct).ConfigureAwait(false))
            return new(chainId, SecurityAuditRetentionOutcome.Held, 0);

        var checkpoint = await store.GetCheckpointAsync(chainId, ct).ConfigureAwait(false);
        var records = new List<SecurityAuditRecord>();
        await foreach (var record in store.ReadAsync(chainId, 1, ct).ConfigureAwait(false))
            records.Add(record);

        // Never destroy evidence from a chain that no longer checks out.
        if (!SecurityAuditChain.VerifyFrom(records, checkpoint).IsValid)
            return new(chainId, SecurityAuditRetentionOutcome.RefusedInvalid, 0);

        var cutoff = clock.GetUtcNow() - options.Value.Retention;
        var expired = records.TakeWhile(r => r.OccurredAt < cutoff).ToList();
        if (expired.Count == 0)
            return new(chainId, SecurityAuditRetentionOutcome.NothingToPurge, 0);

        try
        {
            await archive.ArchiveAsync(chainId, expired, ct).ConfigureAwait(false);
        }
        catch (SecurityAuditArchiveNotConfiguredException)
        {
            return new(chainId, SecurityAuditRetentionOutcome.RefusedNoArchive, 0);
        }

        var last = expired[^1];
        try
        {
            await store.PurgeAsync(new SecurityAuditHead(chainId, last.Sequence, last.Hash), ct).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            // The entries were archived but the store keeps them: the archive now holds a copy, the live chain is unchanged.
            return new(chainId, SecurityAuditRetentionOutcome.RefusedStore, 0);
        }

        return new(chainId, SecurityAuditRetentionOutcome.Purged, expired.Count);
    }
}

/// <summary>What retention did to one chain.</summary>
public enum SecurityAuditRetentionOutcome
{
    /// <summary>The expired entries were archived and purged.</summary>
    Purged,

    /// <summary>The chain is on legal hold; nothing was touched.</summary>
    Held,

    /// <summary>No entry is older than the retention period.</summary>
    NothingToPurge,

    /// <summary>The chain failed verification; nothing was purged.</summary>
    RefusedInvalid,

    /// <summary>No archive is configured; nothing was purged.</summary>
    RefusedNoArchive,

    /// <summary>The store cannot purge (the entries were archived, the live chain is unchanged).</summary>
    RefusedStore,
}

/// <summary>The outcome for one chain and how many entries it purged.</summary>
public sealed record SecurityAuditRetentionResult(Guid ChainId, SecurityAuditRetentionOutcome Outcome, int Purged);
