namespace Modulus.AspNetCore.Idempotency;

using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

/// <summary>
/// Deduplicates unsafe requests by a client-supplied idempotency key. The first
/// request for a key is processed and its response buffered; concurrent duplicates
/// get 409 while it runs, and later duplicates get the original response replayed.
/// A key reused with a different payload is rejected with 422. Failed (5xx) and
/// faulted requests release the claim so a genuine retry can run again.
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next, IOptions<IdempotencyOptions> options)
{
    // Transport-managed headers must not be replayed verbatim — the server sets
    // them for the actual replay response.
    private static readonly HashSet<string> ExcludedHeaders =
        new(StringComparer.OrdinalIgnoreCase) { "Transfer-Encoding", "Content-Length" };

    private readonly IdempotencyOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context, IIdempotencyStore store)
    {
        if (!IsGuardedMethod(context.Request.Method))
        {
            await next(context);
            return;
        }

        var key = context.Request.Headers[_options.HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key))
        {
            if (_options.RequireKey)
            {
                await WriteProblemAsync(context, StatusCodes.Status400BadRequest,
                    "Idempotency key required",
                    $"This endpoint requires an '{_options.HeaderName}' header.");
                return;
            }

            await next(context);
            return;
        }

        if (key.Length > _options.MaxKeyLength)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest,
                "Invalid idempotency key",
                $"The '{_options.HeaderName}' header exceeds {_options.MaxKeyLength} characters.");
            return;
        }

        var scopedKey = BuildScopedKey(context, key);
        var fingerprint = await ComputeFingerprintAsync(context);
        var result = await store.TryBeginAsync(scopedKey, fingerprint, context.RequestAborted);

        switch (result.Status)
        {
            case IdempotencyStatus.Completed:
                if (IsFingerprintMismatch(result.Fingerprint, fingerprint))
                {
                    await WriteReuseConflictAsync(context);
                    return;
                }

                await ReplayAsync(context, result.Response!);
                return;

            case IdempotencyStatus.InProgress:
                if (IsFingerprintMismatch(result.Fingerprint, fingerprint))
                {
                    await WriteReuseConflictAsync(context);
                    return;
                }

                await WriteProblemAsync(context, StatusCodes.Status409Conflict,
                    "Request in progress",
                    "A request with this idempotency key is already being processed. Retry shortly.");
                return;

            default:
                await ProcessAndCaptureAsync(context, store, scopedKey);
                return;
        }
    }

    private async Task ProcessAndCaptureAsync(HttpContext context, IIdempotencyStore store, string scopedKey)
    {
        // Buffer the response so it can be cached and replayed. Nothing is flushed
        // to the client until we copy the buffer back, so status/headers stay mutable.
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
        }
        catch
        {
            context.Response.Body = originalBody;
            await store.AbandonAsync(scopedKey, CancellationToken.None);
            throw;
        }

        context.Response.Body = originalBody;

        if (IsCacheable(context.Response.StatusCode))
        {
            var cached = new CachedResponse(
                context.Response.StatusCode,
                SnapshotHeaders(context.Response),
                buffer.ToArray());
            await store.CompleteAsync(scopedKey, cached, CancellationToken.None);
        }
        else
        {
            // Non-cacheable outcome (e.g. 5xx) — drop the claim so a retry re-runs.
            await store.AbandonAsync(scopedKey, CancellationToken.None);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, context.RequestAborted);
    }

    private async Task ReplayAsync(HttpContext context, CachedResponse cached)
    {
        context.Response.StatusCode = cached.StatusCode;
        foreach (var (name, value) in cached.Headers)
            context.Response.Headers[name] = value;
        context.Response.Headers[_options.ReplayHeaderName] = "true";
        await context.Response.Body.WriteAsync(cached.Body, context.RequestAborted);
    }

    private bool IsGuardedMethod(string method)
    {
        foreach (var guarded in _options.Methods)
            if (string.Equals(guarded, method, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private bool IsFingerprintMismatch(string? stored, string current)
        => _options.ValidateRequestMatch && stored is not null && stored != current;

    private static bool IsCacheable(int statusCode) => statusCode is >= 200 and <= 299;

    private static string BuildScopedKey(HttpContext context, string key)
    {
        // Scope by tenant so keys can't collide (or leak responses) across tenants.
        var tenant = context.RequestServices.GetService<ICurrentTenant>()?.TenantId;
        return tenant is { } id ? $"{id}:{key}" : key;
    }

    private static async Task<string> ComputeFingerprintAsync(HttpContext context)
    {
        var request = context.Request;
        request.EnableBuffering();

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(Encoding.UTF8.GetBytes(
            $"{request.Method}\n{request.Path}\n{request.QueryString}\n"));

        request.Body.Position = 0;
        var rented = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(rented)) > 0)
            hasher.AppendData(rented.AsSpan(0, read));
        request.Body.Position = 0;

        return Convert.ToHexString(hasher.GetHashAndReset());
    }

    private static Dictionary<string, string> SnapshotHeaders(HttpResponse response)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers)
            if (!ExcludedHeaders.Contains(header.Key))
                snapshot[header.Key] = header.Value.ToString();
        return snapshot;
    }

    private Task WriteReuseConflictAsync(HttpContext context)
        => WriteProblemAsync(context, StatusCodes.Status422UnprocessableEntity,
            "Idempotency key reuse",
            "This idempotency key was already used with a different request.");

    private static Task WriteProblemAsync(HttpContext context, int status, string title, string detail)
        => Results.Problem(title: title, detail: detail, statusCode: status).ExecuteAsync(context);
}
