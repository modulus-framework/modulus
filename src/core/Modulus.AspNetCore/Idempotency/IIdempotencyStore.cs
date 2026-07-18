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
    private IdempotencyResult(IdempotencyStatus status, CachedResponse? response, string? fingerprint)
    {
        Status = status;
        Response = response;
        Fingerprint = fingerprint;
    }

    public IdempotencyStatus Status { get; }
    public CachedResponse? Response { get; }
    public string? Fingerprint { get; }

    public static IdempotencyResult Started() => new(IdempotencyStatus.Started, null, null);

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
