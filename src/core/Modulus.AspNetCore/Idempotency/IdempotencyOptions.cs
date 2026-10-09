namespace Modulus.AspNetCore.Idempotency;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Binds from the <c>Idempotency</c> configuration section. Backs
/// <see cref="IdempotencyExtensions.AddModulusIdempotency"/> — safe request
/// replay keyed by a client-supplied <see cref="HeaderName"/> header.
/// </summary>
public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    /// <summary>Header the client sends a unique key in. Defaults to <c>Idempotency-Key</c>.</summary>
    public string HeaderName { get; set; } = "Idempotency-Key";

    /// <summary>Header stamped on replayed responses so callers can tell a cached
    /// reply from a fresh one. Defaults to <c>Idempotency-Replayed</c>.</summary>
    public string ReplayHeaderName { get; set; } = "Idempotency-Replayed";

    /// <summary>HTTP methods the middleware guards. Naturally-idempotent verbs
    /// (GET/HEAD) are never guarded. Defaults to POST and PATCH.</summary>
    public string[] Methods { get; set; } = ["POST", "PATCH"];

    /// <summary>
    /// Path prefixes the middleware never guards. Defaults to <c>/graphql</c>: GraphQL sends queries and mutations through
    /// one POST endpoint, so a required key would reject every read and a supplied one would replay a stale query.
    /// gRPC calls (<c>application/grpc</c>) are always skipped; a gRPC client retries with its own policy.
    /// </summary>
    public string[] ExcludedPaths { get; set; } = ["/graphql"];

    /// <summary>When true, a guarded request without an idempotency key is rejected
    /// with 400. When false (default), keyless requests pass through untouched.</summary>
    public bool RequireKey { get; set; }

    /// <summary>Reject a key that is reused with a different request payload/target
    /// with 422 instead of replaying the original response. Defaults to true.</summary>
    public bool ValidateRequestMatch { get; set; } = true;

    /// <summary>Longest accepted key, in characters. Guards against abuse of the
    /// store. Defaults to 255.</summary>
    public int MaxKeyLength { get; set; } = 255;

    /// <summary>How long a completed response is retained for replay, in seconds.
    /// Defaults to 24 hours. Must be positive — zero/negative would expire
    /// every claim instantly and silently disable dedup (including the 409
    /// in-progress path); validated at startup.</summary>
    [Range(1, int.MaxValue)]
    public int RetentionSeconds { get; set; } = 86_400;

    /// <summary>
    /// How long a claim for a request that is still running holds its key, in
    /// seconds; while it holds, duplicates are answered 409. Defaults to 5
    /// minutes. Only the <b>completed</b> response is kept for
    /// <see cref="RetentionSeconds"/>: an in-progress claim used to live that
    /// long too, so a node that crashed mid-request locked its key out (409 on
    /// every retry) for a whole day. Set it above the longest guarded request
    /// takes: a request still running when its lease runs out can be started
    /// a second time by a retry. Capped at <see cref="RetentionSeconds"/>.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int InProgressLeaseSeconds { get; set; } = 300;

    /// <summary>The claim lifetime actually used: <see cref="InProgressLeaseSeconds"/>, capped at <see cref="RetentionSeconds"/>.</summary>
    internal TimeSpan InProgressLease
        => TimeSpan.FromSeconds(Math.Min(InProgressLeaseSeconds, RetentionSeconds));

    /// <summary>
    /// Largest response body cached for replay, in bytes. Responses larger
    /// than this are NOT cached (the request still runs; a retry re-runs it).
    /// Caps per-key memory pressure in the in-memory store. Defaults to
    /// 1 MB.
    /// </summary>
    public int MaxResponseBytes { get; set; } = 1024 * 1024;

    /// <summary>Minimum accepted value floor for <see cref="MaxResponseBytes"/>.</summary>
    public const int MinResponseBytes = 1;
}
