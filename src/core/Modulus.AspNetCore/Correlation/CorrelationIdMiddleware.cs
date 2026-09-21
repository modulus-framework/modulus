namespace Modulus.AspNetCore.Correlation;

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

/// <summary>
/// Establishes a correlation id for the current request: adopts the inbound
/// <see cref="CorrelationOptions.HeaderName"/> header when present and
/// well-formed (length-capped, safe charset), otherwise derives one (trace id
/// or GUID). The id is pushed into
/// <see cref="ICorrelationContext"/> for the request's async flow, tagged onto
/// the current <see cref="Activity"/> for trace visibility, and echoed on the
/// response so callers can record it.
/// </summary>
public sealed class CorrelationIdMiddleware(
    RequestDelegate next,
    ICorrelationContext correlation,
    IOptions<CorrelationOptions> options)
{
    private readonly CorrelationOptions _options = options.Value;

    // Inbound ids are adopted verbatim into the async flow, Activity tags,
    // response headers, and logs — validate before adopting so an oversized
    // or confusing caller-supplied value cannot pollute observability
    // systems. Invalid values fall back to a derived id (never a 400: the
    // header is advisory, not a contract).
    private const int MaxCorrelationIdLength = 128;

    private static bool IsValidCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxCorrelationIdLength)
            return false;

        foreach (var c in value)
        {
            if (!(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':'))
                return false;
        }
        return true;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var header = _options.HeaderName;

        var incoming = context.Request.Headers[header].FirstOrDefault();
        var id = IsValidCorrelationId(incoming)
            ? incoming!
            : _options.UseTraceIdWhenMissing && Activity.Current is { } activity
                ? activity.TraceId.ToString()
                : Guid.NewGuid().ToString("N");

        using var _ = correlation.BeginScope(id);
        Activity.Current?.SetTag("correlation.id", id);

        if (_options.IncludeInResponse)
        {
            // Set just before the response is sent so a later handler can't drop it.
            context.Response.OnStarting(static state =>
            {
                var (response, headerName, value) = ((HttpResponse, string, string))state;
                response.Headers[headerName] = value;
                return Task.CompletedTask;
            }, (context.Response, header, id));
        }

        await next(context);
    }
}
