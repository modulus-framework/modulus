namespace Modulus.Bff.Middleware;

using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

/// <summary>
/// The per-client edge rules, applied to every endpoint carrying <see cref="BffClientMetadata"/>
/// (session endpoints, aggregators and YARP routes alike):
/// <list type="bullet">
/// <item>web: the CSRF header (<c>X-CSRF: 1</c>) is required, else <c>401</c></item>
/// <item>mobile: the app-version gate (<c>426</c>) and weak ETags on JSON GETs (<c>304</c>)</item>
/// <item>partner: unsafe methods need an <c>Idempotency-Key</c>, else <c>400</c></item>
/// </list>
/// It also stamps <c>bff.client</c> on the current activity, and turns
/// <see cref="BffSectionFailedException"/> into <c>502</c>.
/// </summary>
internal sealed class BffMiddleware(RequestDelegate next, IOptionsMonitor<BffClientOptions> clients, ILogger<BffMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<BffClientMetadata>() is not { } client)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        Activity.Current?.SetTag(BffClientContext.ActivityTag, client.Name);
        var options = clients.Get(client.Name);

        switch (client.Kind)
        {
            case BffClientKind.Web when endpoint.Metadata.GetMetadata<BffSkipCsrfMetadata>() is null
                && !IsEventStreamRead(context, endpoint)
                && !string.Equals(context.Request.Headers[options.CsrfHeaderName], options.CsrfHeaderValue, StringComparison.Ordinal):
                await Problem(context, StatusCodes.Status401Unauthorized, "csrf_header_missing",
                    $"BFF calls must carry the '{options.CsrfHeaderName}: {options.CsrfHeaderValue}' header.").ConfigureAwait(false);
                return;

            case BffClientKind.Mobile when !AppVersionAllowed(context, options, out var minimum):
                context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                await context.Response.WriteAsJsonAsync(new { error = "upgrade_required", minimumVersion = minimum }).ConfigureAwait(false);
                return;

            case BffClientKind.Partner when options.RequireIdempotencyKey
                && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
                && !HttpMethods.IsOptions(context.Request.Method)
                && string.IsNullOrWhiteSpace(context.Request.Headers[options.IdempotencyHeaderName]):
                await Problem(context, StatusCodes.Status400BadRequest, "idempotency_key_required",
                    $"Unsafe requests must carry an '{options.IdempotencyHeaderName}' header.").ConfigureAwait(false);
                return;
        }

        try
        {
            if (client.Kind == BffClientKind.Mobile && options.EnableETags && UsesETag(context.Request))
                await WithETagAsync(context).ConfigureAwait(false);
            else
                await next(context).ConfigureAwait(false);
        }
        catch (BffSectionFailedException ex) when (!context.Response.HasStarted)
        {
            logger.LogWarning(ex, "Required BFF section {Section} failed", ex.Section);
            context.Response.Clear();
            await Problem(context, StatusCodes.Status502BadGateway, "upstream_failed", $"The '{ex.Section}' data is unavailable.").ConfigureAwait(false);
        }
    }

    internal static bool AppVersionAllowed(HttpContext context, BffClientOptions options, out string? minimum)
    {
        var platform = context.Request.Headers[BffDefaults.AppPlatformHeader].ToString();
        minimum = platform.Length > 0 && options.MinimumAppVersionByPlatform.TryGetValue(platform, out var perPlatform)
            ? perPlatform
            : options.MinimumAppVersion;

        var header = context.Request.Headers[BffDefaults.AppVersionHeader].ToString();
        var version = ParseVersion(header);
        if (version is null)
            return !options.RequireAppVersion;
        return minimum is null || ParseVersion(minimum) is not { } min || version >= min;
    }

    /// <summary>Parses <c>2.3</c>, <c>2.3.1</c> or <c>2.3.1-beta+42</c> (pre-release/build suffixes ignored).</summary>
    internal static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var core = value.Trim().TrimStart('v', 'V');
        var cut = core.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0)
            core = core[..cut];
        if (!core.Contains('.', StringComparison.Ordinal))
            core += ".0";
        return Version.TryParse(core, out var parsed) ? parsed : null;
    }

    private static bool IsEventStreamRead(HttpContext context, Endpoint endpoint)
        => endpoint.Metadata.GetMetadata<BffEventStreamMetadata>() is not null
           && HttpMethods.IsGet(context.Request.Method)
           && context.Request.Headers.Accept.ToString().Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);

    private static bool UsesETag(HttpRequest request)
        => HttpMethods.IsGet(request.Method)
           && !request.Headers.Accept.ToString().Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);

    private async Task WithETagAsync(HttpContext context)
    {
        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = original;
        }

        var response = context.Response;
        var isJson = response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
        if (response.StatusCode == StatusCodes.Status200OK && isJson && buffer.Length > 0)
        {
            if (!response.Headers.ContainsKey(HeaderNames.ETag))
            {
                var hash = SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
                response.Headers.ETag = "W/\"" + Convert.ToBase64String(hash, 0, 16).TrimEnd('=') + "\"";
            }

            if (Matches(context.Request.Headers.IfNoneMatch, response.Headers.ETag.ToString()))
            {
                response.StatusCode = StatusCodes.Status304NotModified;
                response.ContentLength = null;
                response.Headers.Remove(HeaderNames.ContentType);
                return;
            }
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(original, context.RequestAborted).ConfigureAwait(false);
    }

    private static bool Matches(Microsoft.Extensions.Primitives.StringValues ifNoneMatch, string etag)
    {
        if (string.IsNullOrEmpty(etag))
            return false;
        var opaque = etag.StartsWith("W/", StringComparison.Ordinal) ? etag[2..] : etag;
        foreach (var value in ifNoneMatch)
        {
            foreach (var candidate in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (candidate == "*")
                    return true;
                var c = candidate.StartsWith("W/", StringComparison.Ordinal) ? candidate[2..] : candidate;
                if (string.Equals(c, opaque, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static Task Problem(HttpContext context, int status, string error, string detail)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { error, detail }, context.RequestAborted);
    }
}
