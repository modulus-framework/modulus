using System.Text.Json;
using Microsoft.Extensions.Options;
using Modulus.AspNetCore.Idempotency;
using StackExchange.Redis;

namespace Modulus.AspNetCore.Redis.Idempotency;

/// <summary>
/// Redis-backed <see cref="IIdempotencyStore"/> — the shared store a
/// multi-instance deployment needs so a client retry that lands on another node
/// still deduplicates. Two keys per idempotency key: a <c>:claim</c> string
/// written with <c>SET NX</c> (the atomic first-caller-wins claim, holding the
/// request fingerprint) and a <c>:data</c> string holding the completed response
/// for replay. Both expire after
/// <see cref="IdempotencyOptions.RetentionSeconds"/>.
/// </summary>
/// <remarks>
/// Failure semantics match the in-memory default: a node that crashes
/// mid-request leaves its claim in place until the retention TTL expires, so
/// retries in that window are answered 409 rather than double-executed —
/// idempotency fails closed.
/// </remarks>
public sealed class RedisIdempotencyStore(
    IConnectionMultiplexer redis,
    IOptions<IdempotencyOptions> options,
    RedisIdempotencyStoreOptions storeOptions)
    : IIdempotencyStore
{
    // Fingerprint travels inside the stored payload so replay and reuse
    // detection survive the round-trip across nodes.
    private sealed record StoredEntry(string Fingerprint, CachedResponse Response);

    private TimeSpan Ttl => TimeSpan.FromSeconds(options.Value.RetentionSeconds);

    private string ClaimKey(string key) => storeOptions.KeyPrefix + key + ":claim";
    private string DataKey(string key) => storeOptions.KeyPrefix + key + ":data";

    /// <inheritdoc />
    public async Task<IdempotencyResult> TryBeginAsync(
        string key, string fingerprint, CancellationToken ct)
    {
        var db = redis.GetDatabase();

        // Completed response already stored? Replay wins over re-claiming even
        // when the claim key has expired ahead of the data key.
        var data = await db.StringGetAsync(DataKey(key));
        if (data.HasValue && Deserialize(data) is { } completed)
            return IdempotencyResult.Completed(completed.Response, completed.Fingerprint);

        // Atomic claim: SET NX — exactly one concurrent caller wins.
        var claimed = await db.StringSetAsync(
            ClaimKey(key), fingerprint, Ttl, keepTtl: false, When.NotExists);
        if (claimed)
            return IdempotencyResult.Started();

        // Lost the claim. The winner may have completed between our two reads.
        data = await db.StringGetAsync(DataKey(key));
        if (data.HasValue && Deserialize(data) is { } justCompleted)
            return IdempotencyResult.Completed(justCompleted.Response, justCompleted.Fingerprint);

        var storedFingerprint = await db.StringGetAsync(ClaimKey(key));
        return IdempotencyResult.InProgress(
            storedFingerprint.HasValue ? storedFingerprint.ToString() : null);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(string key, CachedResponse response, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        var claimFingerprint = await db.StringGetAsync(ClaimKey(key));
        var entry = new StoredEntry(
            claimFingerprint.HasValue ? claimFingerprint.ToString() : string.Empty,
            response);
        // When.NotExists: if our claim expired and another node re-claimed and
        // is (or was) processing this key, its completion wins — a late
        // completer must not clobber the current owner's stored response
        // (which TryBeginAsync would otherwise replay to other callers).
        await db.StringSetAsync(
            DataKey(key), JsonSerializer.Serialize(entry), Ttl, keepTtl: false, When.NotExists);
    }

    /// <inheritdoc />
    public async Task AbandonAsync(string key, CancellationToken ct)
    {
        var db = redis.GetDatabase();
        await db.KeyDeleteAsync(ClaimKey(key));
    }

    private static StoredEntry? Deserialize(RedisValue value)
    {
        try
        {
            return JsonSerializer.Deserialize<StoredEntry>(value.ToString());
        }
        catch (JsonException)
        {
            // A corrupt entry must not poison the key forever — treat as absent
            // so the request re-claims and re-executes.
            return null;
        }
    }
}

/// <summary>Provider-specific settings for <see cref="RedisIdempotencyStore"/>.</summary>
public sealed class RedisIdempotencyStoreOptions
{
    /// <summary>Prefix applied to every Redis key. Defaults to <c>modulus:idem:</c>.</summary>
    public string KeyPrefix { get; set; } = "modulus:idem:";
}
