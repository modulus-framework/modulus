namespace Modulus.AspNetCore.Http;

/// <summary>
/// Standardised API response envelope for all successful API calls.
/// </summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    public string? TraceId { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message
    };
}

/// <summary>
/// Standardised API error response.
/// </summary>
public sealed class ApiErrorResponse
{
    public bool Success { get; init; } = false;
    public string Message { get; init; } = null!;
    public ApiErrorDetail[]? Errors { get; init; }
    public string? TraceId { get; init; }
}

/// <summary>
/// Individual error detail within an <see cref="ApiErrorResponse"/>.
/// </summary>
public sealed class ApiErrorDetail
{
    public string Code { get; init; } = null!;
    public string? Property { get; init; }
    public string Message { get; init; } = null!;
}

/// <summary>
/// Factory helpers for building standardised responses.
/// </summary>
public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? message = null)
        => ApiResponse<T>.Ok(data, message);

    public static ApiErrorResponse Fail(
        string message,
        ApiErrorDetail[]? errors = null,
        string? traceId = null) => new()
        {
            Message = message,
            Errors = errors,
            TraceId = traceId
        };

    public static ApiErrorResponse Fail(
        string code,
        string message,
        string? property = null) => new()
        {
            Message = message,
            Errors = [new ApiErrorDetail { Code = code, Property = property, Message = message }]
        };
}
