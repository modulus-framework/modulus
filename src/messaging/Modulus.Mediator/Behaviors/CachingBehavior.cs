using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;

namespace Modulus.Mediator.Behaviors;

using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

/// <summary>
/// Pipeline behavior that caches query results when the request class is
/// decorated with <see cref="CacheForAttribute"/>.
/// </summary>
/// <remarks>
/// Cache keys are scoped by the ambient tenant (via
/// <see cref="ICurrentTenant"/>) — without that, a query executed for tenant A
/// would be served verbatim to tenant B whenever the serialised request
/// parameters matched. Host-scope queries share a single "host" partition.
/// An <b>unresolved</b> tenant (multi-tenancy is on but no tenant resolved —
/// a missing header, a misconfigured resolver) gets its own "unresolved"
/// partition, distinct from "host": conflating the two would let an
/// unresolved caller be served the host's "sees every tenant" cached result,
/// the same fail-closed distinction <see cref="ICurrentTenant.IsHost"/>
/// itself draws.
/// Keys are additionally scoped by the calling user (via
/// <see cref="ICurrentUser"/>): cached results frequently embed per-user
/// authorization filtering (visibility, redaction), so serving one user's
/// cached page to another user in the same tenant would leak data across
/// users. Anonymous callers share an "anon" partition.
/// <para>
/// When a <see cref="HybridCache"/> is registered (<c>AddModulusFusionCache</c>)
/// it is used instead of <see cref="IMemoryCache"/>: entries are shared across
/// nodes through L2, concurrent misses run the handler once, and the
/// attribute's <see cref="CacheForAttribute.Tags"/> (tenant-scoped through
/// <see cref="Modulus.Caching.CacheKeys"/>) let <see cref="CacheInvalidationBehavior{TRequest,TResponse}"/>
/// evict them.
/// </para>
/// </remarks>
public sealed class CachingBehavior<TRequest, TResponse>(
    IMemoryCache cache,
    ICurrentTenant? currentTenant = null,
    ICurrentUser? currentUser = null,
    HybridCache? hybridCache = null) : IPipelineBehavior<TRequest, TResponse>
{
    // The attribute is fixed per request type; read it once per closed generic.
    private static readonly CacheForAttribute? s_attr =
        typeof(TRequest).GetCustomAttribute<CacheForAttribute>();

    // Distinct from the IMemoryCache keys, and from entries an earlier version stored as the bare value in a shared L2.
    private const string OutcomeKeyPrefix = "outcome:";

    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        if (s_attr is null)
            return await next();

        var attr = s_attr;

        var key = BuildCacheKey(request);

        if (hybridCache is not null)
        {
            var duration = TimeSpan.FromSeconds(attr.Seconds);
            var tags = attr.Tags is { Length: > 0 }
                ? Array.ConvertAll(attr.Tags, t => Modulus.Caching.CacheKeys.Tag(currentTenant, t))
                : null;
            // The entry holds the handler's outcome, not just its value: a domain error (not found, forbidden, ...) is
            // an answer, not a failure. Thrown from the factory, it would let fail-safe serve the previous value, so an
            // entity deleted after an invalidation (which only expires the entry) kept coming back. Other exceptions
            // still propagate, and fail-safe covers them as designed (database down, timeouts).
            var outcome = await hybridCache.GetOrCreateAsync(
                OutcomeKeyPrefix + key,
                next,
                static async (handler, _) => await CachedOutcome<TResponse>.CaptureAsync(handler),
                new HybridCacheEntryOptions { Expiration = duration, LocalCacheExpiration = duration },
                tags,
                ct);
            return outcome.Unwrap();
        }

        if (cache.TryGetValue(key, out TResponse? cached) && cached is not null)
            return cached;

        var result = await next();
        cache.Set(key, result, TimeSpan.FromSeconds(attr.Seconds));
        return result;
    }

    private string BuildCacheKey(TRequest request)
    {
        var type = typeof(TRequest).FullName ?? typeof(TRequest).Name;
        // Use JSON to serialise the request — ensures different parameter
        // values produce different keys.
        var payload = JsonSerializer.Serialize(request);

        var tenantPart = currentTenant switch
        {
            null => "host",
            { IsHost: true } => "host",
            { TenantId: { } tenantId } => tenantId.ToString(),
            _ => "unresolved",
        };
        var userPart = currentUser?.UserId?.ToString() ?? "anon";
        return $"modulus:cache:t:{tenantPart}:u:{userPart}:{type}:{payload}";
    }
}

/// <summary>
/// A cached query outcome: the value, or the domain error the handler reported (replayed to every caller until the entry
/// expires or its tags are invalidated). Serializable, since an L2 cache stores it.
/// </summary>
internal sealed class CachedOutcome<TResponse>
{
    public TResponse? Value { get; init; }

    /// <summary>The error kind (the exception type's name without "Exception"), or null for a value.</summary>
    public string? Error { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<string>? Errors { get; init; }

    /// <summary>The missing permission or the disabled feature.</summary>
    public string? Detail { get; init; }

    public static async Task<CachedOutcome<TResponse>> CaptureAsync(RequestHandlerDelegate<TResponse> handler)
    {
        try
        {
            return new CachedOutcome<TResponse> { Value = await handler() };
        }
        catch (NotFoundException ex)
        {
            return new CachedOutcome<TResponse> { Error = "NotFound", Message = ex.Message };
        }
        catch (ValidationException ex)
        {
            return new CachedOutcome<TResponse> { Error = "Validation", Errors = ex.Errors };
        }
        catch (UnauthorizedException)
        {
            return new CachedOutcome<TResponse> { Error = "Unauthorized" };
        }
        catch (ForbiddenException ex)
        {
            return new CachedOutcome<TResponse> { Error = "Forbidden", Detail = ex.Permission };
        }
        catch (FeatureDisabledException ex)
        {
            return new CachedOutcome<TResponse> { Error = "FeatureDisabled", Detail = ex.Feature };
        }
        catch (ConflictException ex)
        {
            return new CachedOutcome<TResponse> { Error = "Conflict", Message = ex.Message };
        }
    }

    public TResponse Unwrap() => Error switch
    {
        null => Value!,
        "NotFound" => throw new NotFoundException(Message ?? string.Empty),
        "Validation" => throw new ValidationException(Errors ?? []),
        "Unauthorized" => throw new UnauthorizedException(),
        "Forbidden" => throw new ForbiddenException(Detail ?? string.Empty),
        "FeatureDisabled" => throw new FeatureDisabledException(Detail ?? string.Empty),
        "Conflict" => throw new ConflictException(Message ?? string.Empty),
        _ => throw new InvalidOperationException($"Unknown cached query outcome '{Error}'."),
    };
}
