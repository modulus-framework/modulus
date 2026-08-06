using Microsoft.AspNetCore.Http;

namespace Modulus.AspNetCore.Http;

using ErrorOr;

public static class ApiResults
{
    /// <summary>
    /// Converts a failed ErrorOr result into an RFC 7807 IResult.
    /// Maps ErrorType to HTTP status code automatically.
    /// </summary>
    public static IResult Problem<T>(ErrorOr<T> result)
    {
        if (!result.IsError)
            throw new InvalidOperationException(
                "Cannot create Problem from a success result.");

        var errors = result.Errors;

        if (errors.Count > 1)
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Multiple errors occurred.",
                extensions: new Dictionary<string, object?>
                {
                    ["errors"] = errors.Select(e => new
                    { e.Code, e.Description, Type = e.Type.ToString() })
                });

        var first = errors[0];
        return Results.Problem(
            statusCode: ToStatusCode(first.Type),
            title: first.Code,
            detail: first.Description);
    }

    private static int ToStatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };
}
