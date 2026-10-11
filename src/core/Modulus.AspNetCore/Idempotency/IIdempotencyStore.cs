namespace Modulus.AspNetCore.Idempotency;

/// <summary>
/// Persistence abstraction for HTTP request idempotency. Implementations claim a
/// key atomically (first caller wins), retain the completed response for replay,
/// and release the claim if a request fails.
/// </summary>
/// <remarks>
/// The default <c>InMemoryIdempotencyStore</c> is <b>per-instance</b> — adequate
/// for a single node, tests, and development. Multi-instance deployments must
/// register a shared implementation (Redis, EF Core, …) so a retry that lands on
/// another node still deduplicates.
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically claims <paramref name="key"/> for the current request.
    /// Returns <see cref="IdempotencyStatus.Started"/> when the caller won the
    /// claim (and must process the request), <see cref="IdempotencyStatus.InProgress"/>
    /// when another request holds the claim, or <see cref="IdempotencyStatus.Completed"/>
    /// with the cached response when the original already finished.
    /// </summary>
    Task<IdempotencyResult> TryBeginAsync(string key, string fingerprint, CancellationToken ct);

    /// <summary>Stores the finished response against a claimed key for later replay.</summary>
    Task CompleteAsync(string key, CachedResponse response, CancellationToken ct);

    /// <summary>Releases a claimed key so the request can be safely retried.</summary>
    Task AbandonAsync(string key, CancellationToken ct);

    /// <summary>
    /// Completes the claim identified by <paramref name="claimToken"/>; a store that issues tokens refuses a completion from
    /// a claim it no longer owns. The default ignores the token.
    /// </summary>
    Task CompleteAsync(string key, string? claimToken, CachedResponse response, CancellationToken ct)
        => CompleteAsync(key, response, ct);

    /// <summary>
    /// Releases the claim identified by <paramref name="claimToken"/>, and only that claim. The default ignores the token.
    /// </summary>
    Task AbandonAsync(string key, string? claimToken, CancellationToken ct)
        => AbandonAsync(key, ct);

    /// <summary>
    /// Evicts expired entries from the store. Called periodically by the
    /// background sweeper to bound memory consumption. Implementations that
    /// use external persistence (Redis, EF Core) may no-op since TTL is
    /// managed at the store level. The <paramref name="cutoff"/> timestamp
    /// identifies entries whose claim or completion has expired.
    /// </summary>
    Task PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken ct)
        => Task.CompletedTask; // default no-op for external stores
}

/// <summary>Outcome of an <see cref="IIdempotencyStore.TryBeginAsync"/> claim.</summary>
public enum IdempotencyStatus
{
    /// <summary>The claim was acquired; the caller owns processing this request.</summary>
    Started,

    /// <summary>An earlier request with the same key is still being processed.</summary>
    InProgress,

    /// <summary>The original request finished; its response is available for replay.</summary>
    Completed,
}

/// <summary>Result of a claim attempt. <see cref="Response"/> is set only when
/// <see cref="Status"/> is <see cref="IdempotencyStatus.Completed"/>;
/// <see cref="Fingerprint"/> carries the stored request fingerprint for reuse
/// detection when the key already existed.</summary>
public sealed class IdempotencyResult
{
    private IdempotencyResult(IdempotencyStatus status, CachedResponse? response, string? fingerprint, string? claimToken = null)
    {
        Status = status;
        Response = response;
        Fingerprint = fingerprint;
        ClaimToken = claimToken;
    }

    public IdempotencyStatus Status { get; }
    public CachedResponse? Response { get; }
    public string? Fingerprint { get; }

    /// <summary>
    /// The token of a <see cref="IdempotencyStatus.Started"/> claim. The middleware hands it back to
    /// <see cref="IIdempotencyStore.CompleteAsync(string, string?, CachedResponse, CancellationToken)"/> and
    /// <see cref="IIdempotencyStore.AbandonAsync(string, string?, CancellationToken)"/>, so a request whose claim was taken
    /// over cannot complete or abandon the new owner's claim. Null for stores that do not issue tokens.
    /// </summary>
    public string? ClaimToken { get; }

    public static IdempotencyResult Started() => new(IdempotencyStatus.Started, null, null);

    /// <summary>A claim won with a <paramref name="claimToken"/> the store will check on completion and abandon.</summary>
    public static IdempotencyResult Started(string claimToken) => new(IdempotencyStatus.Started, null, null, claimToken);

    public static IdempotencyResult InProgress(string? fingerprint)
        => new(IdempotencyStatus.InProgress, null, fingerprint);

    public static IdempotencyResult Completed(CachedResponse response, string? fingerprint)
        => new(IdempotencyStatus.Completed, response, fingerprint);
}

/// <summary>A captured HTTP response retained for idempotent replay.</summary>
/// <param name="StatusCode">The response status code.</param>
/// <param name="Headers">Response headers to restore on replay (transport-managed
/// headers excluded by the middleware).</param>
/// <param name="Body">The buffered response body.</param>
public sealed record CachedResponse(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body);
