namespace Modulus.Messaging;

/// <summary>A consumer did not finish within its <see cref="ConsumerOptions.ConsumeTimeout"/>.</summary>
public sealed class ConsumeTimeoutException : TimeoutException
{
    public ConsumeTimeoutException(string endpoint, TimeSpan timeout)
        : base($"Consumer on endpoint '{endpoint}' did not finish within {timeout}.")
    {
        Endpoint = endpoint;
        Timeout = timeout;
    }

    public string Endpoint { get; }

    public TimeSpan Timeout { get; }
}
