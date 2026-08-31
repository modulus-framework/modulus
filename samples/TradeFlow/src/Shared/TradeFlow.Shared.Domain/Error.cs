namespace TradeFlow.Shared.Domain;

public record Error(string Code, string Message, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);
    public static readonly Error NullValue = new("General.NullValue", "Null value was provided", ErrorType.Failure);

    public static Error NotFound(string code, string message)
        => new(code, message, ErrorType.NotFound);

    public static Error Validation(string code, string message)
        => new(code, message, ErrorType.Validation);

    public static Error Conflict(string code, string message)
        => new(code, message, ErrorType.Conflict);

    public static Error Internal(string code, string message)
        => new(code, message, ErrorType.Internal);

    public static Error Unauthorized(string code, string message)
        => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message)
        => new(code, message, ErrorType.Forbidden);

    public static Error BusinessRule(string code, string message)
        => new(code, message, ErrorType.BusinessRule);

    public static Error Failure(string code, string message)
        => new(code, message, ErrorType.Failure);

    public static Error Problem(string code, string message)
        => new(code, message, ErrorType.Problem);
}
