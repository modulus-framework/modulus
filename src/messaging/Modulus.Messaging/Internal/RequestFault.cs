namespace Modulus.Messaging.Internal;

/// <summary>Receives remote faults on this process's reply endpoint and fails the waiting request.</summary>
internal sealed class RequestFaultReceiver(RequestRegistry registry) : IConsumer<RequestFault>
{
    public Task Consume(ConsumeContext<RequestFault> context)
    {
        registry.Fail(context.Message.RequestId, context.Message.ExceptionType, context.Message.Message);
        return Task.CompletedTask;
    }
}
