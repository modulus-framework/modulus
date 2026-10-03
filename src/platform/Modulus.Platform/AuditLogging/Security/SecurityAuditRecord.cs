namespace Modulus.AuditLogging.Security;

/// <summary>
/// One sequenced, hashed entry of a tenant's security audit chain. <see cref="Hash"/> covers every other field,
/// <see cref="PreviousHash"/> included, so editing, deleting or reordering an entry breaks every later hash.
/// </summary>
public sealed record SecurityAuditRecord
{
    /// <summary>The chain: the company id, or <see cref="Guid.Empty"/> for the host chain.</summary>
    public required Guid ChainId { get; init; }

    /// <summary>1-based position in the chain, contiguous.</summary>
    public required long Sequence { get; init; }

    /// <summary>When the event happened (UTC, millisecond precision so every database round-trips it).</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    public required string Category { get; init; }

    public required string Action { get; init; }

    public required string Outcome { get; init; }

    public string? Actor { get; init; }

    public string? Target { get; init; }

    public string? CorrelationId { get; init; }

    public IReadOnlyDictionary<string, string?> Details { get; init; } = new Dictionary<string, string?>();

    /// <summary>The previous entry's <see cref="Hash"/>; <see cref="SecurityAuditChain.GenesisHash"/> for the first.</summary>
    public required string PreviousHash { get; init; }

    /// <summary>Lower-case hex SHA-256 of the canonical JSON of this entry (<see cref="SecurityAuditChain.ComputeHash"/>).</summary>
    public required string Hash { get; init; }
}

/// <summary>The last entry of a chain: what an anchor records so a rewritten chain is detectable.</summary>
public sealed record SecurityAuditHead(Guid ChainId, long Sequence, string Hash);

/// <summary>The result of <see cref="SecurityAuditStoreExtensions.VerifyChainAsync"/>.</summary>
/// <param name="IsValid">Every entry's hash, link and sequence checked out (and matched the anchor when given).</param>
/// <param name="Count">Entries read.</param>
/// <param name="Head">The last valid entry, null for an empty chain.</param>
/// <param name="BrokenAt">The first sequence that failed, when invalid.</param>
/// <param name="Problem">Why it failed, when invalid.</param>
public sealed record SecurityAuditVerification(
    bool IsValid, long Count, SecurityAuditHead? Head, long? BrokenAt = null, string? Problem = null);
