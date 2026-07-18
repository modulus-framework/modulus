namespace Modulus.AspNetCore.Correlation;

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;

/// <summary>
/// Establishes a correlation id for the current request: adopts the inbound
/// <see cref="CorrelationOptions.HeaderName"/> header when present, otherwise
/// derives one (trace id or GUID). The id is pushed into
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

    public async Task InvokeAsync(HttpContext context)
    {
        var header = _options.HeaderName;

        var id = context.Request.Headers[header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id))
            id = _options.UseTraceIdWhenMissing && Activity.Current is { } activity
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
