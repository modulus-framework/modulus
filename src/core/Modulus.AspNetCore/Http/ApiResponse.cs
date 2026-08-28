namespace Modulus.AspNetCore.Http;

/// <summary>
/// Standardised API response envelope for all successful API calls.
/// </summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message
    };
}

/// <summary>
/// Factory helpers for building standardised responses.
/// </summary>
/// <remarks>
/// There is deliberately no error envelope here: every framework error path
/// (exception handler, binding, validation, <c>SendErrorAsync</c>) emits an
/// RFC 7807 problem response so clients handle exactly one error shape.
/// </remarks>
public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? message = null)
        => ApiResponse<T>.Ok(data, message);
}
