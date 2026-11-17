using Modulus.Messaging;

namespace Modulus.Messaging.Sagas;

/// <summary>Tracks the one open request of a saga instance, so a late timeout is dropped and a reply closes the request (FR-17).</summary>
internal static class SagaRequests
{
    /// <summary>True when the message is a timeout that no longer belongs to the saga's open request (or the saga is gone).</summary>
    internal static bool IsStaleTimeout(SagaInstance? saga, object message) =>
        message is SagaRequestTimedOut timeout && (saga is null || saga.OpenRequestTimeoutId != timeout.TimeoutId);

    /// <summary>
    /// Closes the open request when this delivery answers it (its request id is the saga's id) or is its timeout. A
    /// later delivery of the closed request's timeout is then stale.
    /// </summary>
    internal static void CloseIfAnswered(SagaInstance saga, object message, IReadOnlyDictionary<string, string> headers)
    {
        if (message is SagaRequestTimedOut)
        {
            saga.OpenRequestTimeoutId = null;
            return;
        }

        if (saga.OpenRequestTimeoutId is null)
        {
            return;
        }

        var answeredId = message switch
        {
            IRequestResponse reply => (Guid?)reply.RequestId,
            RequestFault fault => fault.RequestId,
            _ => headers.TryGetValue(MessagingHeaders.RequestId, out var requestText) && Guid.TryParse(requestText, out var parsed) ? parsed : null,
        };
        if (answeredId == saga.CorrelationId)
        {
            saga.OpenRequestTimeoutId = null;
        }
    }
}
