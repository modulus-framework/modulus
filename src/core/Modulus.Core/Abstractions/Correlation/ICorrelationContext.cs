namespace Modulus.Core.Abstractions;

/// <summary>
/// Ambient accessor for the correlation id of the current logical operation — a
/// request, a background job, or a message consumer. The id ties together every
/// log line, trace span, and downstream HTTP call made while handling that
/// operation, so a single request can be followed end-to-end across services.
/// </summary>
/// <remarks>
/// Implemented by <c>Modulus.Core.Correlation.CorrelationContext</c> over a
/// static <see cref="System.Threading.AsyncLocal{T}"/>, so a correlation id set
/// on one async flow is visible to all its continuations (including code that
/// opens its own DI scope) while staying invisible to unrelated flows. The
/// inbound <c>CorrelationIdMiddleware</c> (Modulus.AspNetCore) establishes it per
/// request; <c>CorrelationIdPropagationHandler</c> forwards it on outbound
/// <see cref="System.Net.Http.HttpClient"/> calls.
/// </remarks>
public interface ICorrelationContext
{
    /// <summary>The current correlation id, or <see langword="null"/> when none is set.</summary>
    string? CorrelationId { get; }

    /// <summary>Whether a correlation id is in scope for the current async flow.</summary>
    bool IsSet { get; }

    /// <summary>
    /// Establishes <paramref name="correlationId"/> as the ambient correlation id
    /// for the current async flow and returns a scope that restores the previous
    /// value when disposed:
    /// <code>using var _ = correlation.BeginScope(id);</code>
    /// Use in message consumers and background jobs that run outside an HTTP
    /// request so their work carries the originating request's id.
    /// </summary>
    IDisposable BeginScope(string correlationId);
}

/// <summary>Well-known correlation header name(s).</summary>
public static class CorrelationHeaders
{
    /// <summary>The default correlation-id header, <c>X-Correlation-ID</c>.</summary>
    public const string Default = "X-Correlation-ID";
}
