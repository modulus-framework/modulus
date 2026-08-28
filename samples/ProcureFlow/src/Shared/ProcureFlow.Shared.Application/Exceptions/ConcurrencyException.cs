namespace ProcureFlow.Shared.Application.Exceptions;

public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(string? message = null, Exception? innerException = null)
        : base(message ?? "A concurrency conflict occurred.", innerException)
    {
    }
}
