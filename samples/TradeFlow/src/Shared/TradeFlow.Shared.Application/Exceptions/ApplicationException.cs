using TradeFlow.Shared.Domain;

namespace TradeFlow.Shared.Application.Exceptions;

public sealed class ApplicationException : Exception
{
    public string ErrorCode { get; }
    public string? RequestName { get; }
    public Error? Error { get; }

    public ApplicationException(string errorCode, string? message = null, Error? error = null, string? requestName = null, Exception? innerException = null)
        : base(message ?? errorCode, innerException)
    {
        ErrorCode = errorCode;
        RequestName = requestName;
        Error = error;
    }
}
