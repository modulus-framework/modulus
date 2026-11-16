namespace Modulus.Messaging;

/// <summary>
/// Sent back to a requester when its request failed on the consumer side (FR-06): the exception type (or the park reason)
/// and its message. A request client gets a <c>RemoteRequestException</c> instead of waiting for a timeout. A saga that
/// asked with <c>SagaContext.Request</c> declares it as an event, correlated by <see cref="RequestId"/>.
/// </summary>
public sealed record RequestFault(Guid RequestId, string ExceptionType, string Message);
