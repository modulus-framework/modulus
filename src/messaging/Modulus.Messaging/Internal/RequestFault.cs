namespace Modulus.Messaging.Internal;

/// <summary>
/// Sent back to a requester when its request failed on the consumer side (FR-06): the exception type (or the park reason)
/// and its message. The requester gets a <see cref="RemoteRequestException"/> instead of waiting for a timeout.
/// </summary>
internal sealed record RequestFault(Guid RequestId, string ExceptionType, string Message);

/// <summary>Receives remote faults on this process's reply endpoint and fails the waiting request.</summary>
internal sealed class RequestFaultReceiver(RequestRegistry registry) : IConsumer<RequestFault>
{
    public Task Consume(ConsumeContext<RequestFault> context)
    {
        registry.Fail(context.Message.RequestId, context.Message.ExceptionType, context.Message.Message);
        return Task.CompletedTask;
    }
}
