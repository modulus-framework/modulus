namespace Modulus.Messaging;

/// <summary>
/// Implemented by a reply, so the bus can apply the response type's encryption attribute to the reply (D6). Its
/// <see cref="ResponseMessage"/> is the response, or null for a message that is not a reply.
/// </summary>
public interface IRequestResponse
{
    /// <summary>The id of the request this reply answers.</summary>
    Guid RequestId { get; }

    object? ResponseMessage => null;
}

/// <summary>The reply to a request: the request's id and the response (FR-06). Sent by <c>ConsumeContext.RespondAsync</c>.</summary>
public sealed record RequestReply<TResponse>(Guid RequestId, TResponse Response) : IRequestResponse
    where TResponse : class
{
    object? IRequestResponse.ResponseMessage => Response;
}

/// <summary>Sends a request and waits for the consumer's response (FR-06).</summary>
public interface IRequestClient
{
    /// <summary>
    /// Sends <paramref name="request"/> to <paramref name="address"/> and waits up to <paramref name="timeout"/> for the
    /// consumer to answer with <c>RespondAsync</c>. The response type must be registered with
    /// <c>MessagingBusBuilder.AddRequestClient</c>. A request is sent at once, not through the outbox: a reply cannot be
    /// stored with the caller's transaction.
    /// </summary>
    Task<TResponse> GetResponse<TRequest, TResponse>(Uri address, TRequest request, TimeSpan timeout, CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class;
}

/// <summary>A request got no response within its timeout: the consumer did not run, failed, or did not respond.</summary>
public sealed class RequestTimeoutException : TimeoutException
{
    public RequestTimeoutException(string address, TimeSpan timeout)
        : base($"No response from '{address}' within {timeout}.")
    {
        Address = address;
        Timeout = timeout;
    }

    public string Address { get; }

    public TimeSpan Timeout { get; }
}

/// <summary>The consumer failed while handling the request (FR-06): the exception type and message it reported.</summary>
public sealed class RemoteRequestException : Exception
{
    public RemoteRequestException(string exceptionType, string message)
        : base($"The remote consumer failed with {exceptionType}: {message}")
    {
        ExceptionType = exceptionType;
    }

    /// <summary>The exception type on the consumer side, or the park reason (for example <c>delivery-limit</c>).</summary>
    public string ExceptionType { get; }
}
