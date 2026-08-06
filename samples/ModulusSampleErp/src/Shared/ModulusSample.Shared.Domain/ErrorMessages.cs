namespace ModulusSample.Shared.Domain;

public static class ErrorMessages
{
    public const string InvalidCredentials = "Invalid email or password.";
    public const string AccountLocked = "Your account has been locked. Please contact support.";
    public const string EmailAlreadyExists = "An account with this email already exists.";
    public const string InvalidToken = "Invalid or expired token. Please log in again.";

    public const string UnexpectedError = "An unexpected error occurred. Please try again later.";
    public const string ServiceUnavailable = "This service is temporarily unavailable. Please try again later.";
    public const string ValidationFailed = "Please check your input and try again.";
}
