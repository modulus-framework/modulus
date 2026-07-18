namespace Modulus.Core.Http;

using Modulus.Core.Abstractions;

/// <summary>
/// Outbound <see cref="DelegatingHandler"/> that copies the current
/// <see cref="ICorrelationContext.CorrelationId"/> onto every request as the
/// <see cref="CorrelationHeaders.Default"/> header, so a downstream service sees
/// the same id and its <c>CorrelationIdMiddleware</c> adopts it. Register it on a
/// client with <c>AddModulusHttpClient</c> (Modulus.Platform), or manually via
/// <c>AddHttpMessageHandler&lt;CorrelationIdPropagationHandler&gt;()</c>.
/// </summary>
/// <remarks>
/// W3C <c>traceparent</c> is already injected automatically by
/// <see cref="System.Net.Http.HttpClient"/> whenever an <see cref="System.Diagnostics.Activity"/>
/// is current, so distributed traces link up without any extra handler. This
/// handler carries the <em>business</em> correlation id, which is independent of
/// the trace id and survives even when tracing is disabled. An id already present
/// on the outgoing request is left untouched.
/// </remarks>
public sealed class CorrelationIdPropagationHandler(
    ICorrelationContext correlation,
    string headerName = CorrelationHeaders.Default) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var id = correlation.CorrelationId;
        if (!string.IsNullOrWhiteSpace(id)
            && !request.Headers.Contains(headerName))
        {
            request.Headers.TryAddWithoutValidation(headerName, id);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
