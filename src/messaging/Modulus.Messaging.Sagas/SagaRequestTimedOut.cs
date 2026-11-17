namespace Modulus.Messaging.Sagas;

/// <summary>
/// Arrives at a saga's endpoint when a request the saga made with <see cref="SagaContext{TSaga,TMessage}.Request{TRequest}(Uri, TRequest, TimeSpan)"/>
/// got no answer in time (FR-17). A saga handles it like any event: it declares it with <c>ICorrelatedBy</c> and an
/// <c>IOrchestrates</c> or <c>When</c> reaction, and decides what the timeout means.
/// </summary>
/// <remarks>
/// A timeout that no longer belongs to the open request is dropped before the saga sees it: the answer arrived first, or a
/// later request replaced this one. An answer (or a fault) for the open request closes it, so its timeout is dropped too.
/// </remarks>
/// <param name="CorrelationId">The saga instance the request belongs to.</param>
/// <param name="TimeoutId">Identifies this request's timeout; the saga keeps the id of its open request.</param>
/// <param name="Address">The endpoint the request was sent to.</param>
/// <param name="RequestType">The full name of the request message type.</param>
public sealed record SagaRequestTimedOut(Guid CorrelationId, Guid TimeoutId, string Address, string RequestType);
