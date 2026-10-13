namespace Modulus.Realtime.Transports;

using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;

/// <summary>One Server-Sent Events frame. <see cref="Data"/> may span several lines.</summary>
internal readonly record struct SseFrame(string Data, string? Event = null, string? Id = null, TimeSpan? Retry = null);

/// <summary>
/// Writes an <c>text/event-stream</c> response from an async sequence of frames. Hand-rolled instead of
/// <c>TypedResults.ServerSentEvents</c> (.NET 10 only) so the same stream works on net8.0 and net10.0.
/// Each frame is flushed as it is written, so a subscriber sees it immediately.
/// </summary>
internal sealed class SseResult(IAsyncEnumerable<SseFrame> frames) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";

        var ct = httpContext.RequestAborted;
        await response.StartAsync(ct).ConfigureAwait(false);
        try
        {
            await foreach (var frame in frames.WithCancellation(ct).ConfigureAwait(false))
            {
                await response.WriteAsync(Format(frame), ct).ConfigureAwait(false);
                await response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The client went away or the token expired; the stream simply ends.
        }
    }

    /// <summary>Formats one frame. Per the SSE spec, each data line gets its own <c>data:</c> prefix.</summary>
    internal static string Format(SseFrame frame)
    {
        var sb = new StringBuilder();
        if (frame.Retry is { } retry)
            sb.Append("retry: ").Append(((long)retry.TotalMilliseconds).ToString(CultureInfo.InvariantCulture)).Append('\n');
        if (frame.Id is { Length: > 0 } id)
            sb.Append("id: ").Append(id).Append('\n');
        if (frame.Event is { Length: > 0 } name)
            sb.Append("event: ").Append(name).Append('\n');

        var lines = frame.Data.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        foreach (var line in lines)
            sb.Append("data: ").Append(line).Append('\n');
        sb.Append('\n');
        return sb.ToString();
    }
}
