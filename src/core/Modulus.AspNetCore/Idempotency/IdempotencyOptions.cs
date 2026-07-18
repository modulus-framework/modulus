namespace Modulus.AspNetCore.Idempotency;

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
    /// Defaults to 24 hours.</summary>
    public int RetentionSeconds { get; set; } = 86_400;
}
