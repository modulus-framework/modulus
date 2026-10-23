namespace Modulus.AuditLogging.Security.Retention;

/// <summary>The outcome of <see cref="SecurityAuditRestoreValidator.ValidateAsync"/>.</summary>
/// <param name="IsValid">True when the archive and the live store form one unbroken chain from the genesis hash.</param>
/// <param name="ArchivedEntries">How many entries the archive holds for the chain.</param>
/// <param name="LiveEntries">How many entries the live store holds after its checkpoint.</param>
/// <param name="Problem">Why the chain could not be proven, when invalid.</param>
public sealed record SecurityAuditRestoreValidation(bool IsValid, long ArchivedEntries, long LiveEntries, string? Problem);

/// <summary>
/// Read-only restore validation (A11-2): checks that a chain's archive and its live store still join into one chain from the genesis
/// hash. The archive must end exactly at the live store's checkpoint, and the live entries must verify from it. Nothing is written:
/// archived entries are never re-inserted into the live chain, because that would rewrite committed history. Any such restore needs
/// the technical spike that the identity plan requires before it can be enabled.
/// </summary>
public sealed class SecurityAuditRestoreValidator(ISecurityAuditStore store, ISecurityAuditArchive archive)
{
    /// <summary>Validates one chain. An archive that cannot be read back makes the chain unprovable, so the result is invalid.</summary>
    public async Task<SecurityAuditRestoreValidation> ValidateAsync(Guid chainId, CancellationToken ct = default)
    {
        var checkpoint = await store.GetCheckpointAsync(chainId, ct).ConfigureAwait(false);
        var genesis = new SecurityAuditHead(chainId, 0, SecurityAuditChain.GenesisHash);

        // The archive is always read, so entries it holds for a chain with no checkpoint are caught. An archive that cannot be read back
        // only matters when something was purged: without a checkpoint nothing needs it.
        var archived = new List<SecurityAuditRecord>();
        try
        {
            archived.AddRange(await archive.ReadAsync(chainId, ct).ConfigureAwait(false));
        }
        catch (NotSupportedException) when (checkpoint is null)
        {
            // Nothing was purged, so there is nothing the unreadable archive would have to prove.
        }
        catch (NotSupportedException ex)
        {
            return new SecurityAuditRestoreValidation(false, 0, 0, ex.Message);
        }

        var live = new List<SecurityAuditRecord>();
        await foreach (var record in store.ReadAsync(chainId, checkpoint is null ? 1 : checkpoint.Sequence + 1, ct).ConfigureAwait(false))
            live.Add(record);

        if (checkpoint is null && archived.Count > 0)
            return Invalid(archived.Count, live.Count, "The archive holds entries, but the store has no checkpoint for them.");

        if (checkpoint is not null && (archived.Count == 0 || archived[^1].Sequence != checkpoint.Sequence || archived[^1].Hash != checkpoint.Hash))
            return Invalid(archived.Count, live.Count, $"The archive does not end at the checkpoint (entry {checkpoint.Sequence}); entries are missing from it.");

        var archiveCheck = SecurityAuditChain.VerifyFrom(archived, genesis);
        if (!archiveCheck.IsValid)
            return Invalid(archived.Count, live.Count, $"The archive does not verify: {archiveCheck.Problem}");

        var liveCheck = SecurityAuditChain.VerifyFrom(live, checkpoint);
        if (!liveCheck.IsValid)
            return Invalid(archived.Count, live.Count, $"The live chain does not verify from its checkpoint: {liveCheck.Problem}");

        return new SecurityAuditRestoreValidation(true, archived.Count, live.Count, null);
    }

    private static SecurityAuditRestoreValidation Invalid(long archived, long live, string problem)
        => new(false, archived, live, problem);
}
