namespace TradeFlow.Shared.Domain;

public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Internal,
    Unauthorized,
    Forbidden,
    BusinessRule,
    Problem
}
