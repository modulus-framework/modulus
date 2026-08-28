using Microsoft.AspNetCore.Http;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Vendors.Presentation;

internal static class EndpointHelper
{
    public static async Task SendFailureAsync(HttpContext httpContext, Result result, CancellationToken ct = default)
    {
        if (result.IsSuccess)
            return;

        var (statusCode, detail) = result.Error.Type switch
        {
            ErrorType.NotFound => (StatusCodes.Status404NotFound, result.Error.Message),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, result.Error.Message),
            ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, result.Error.Message),
            ErrorType.Forbidden => (StatusCodes.Status403Forbidden, result.Error.Message),
            ErrorType.BusinessRule => (StatusCodes.Status422UnprocessableEntity, result.Error.Message),
            _ => (StatusCodes.Status400BadRequest, result.Error.Message)
        };

        var problem = Results.Problem(detail: detail, statusCode: statusCode);
        await problem.ExecuteAsync(httpContext);
    }

    public static async Task ResolveAsync<T>(
        HttpContext httpContext,
        Result<T> result,
        CancellationToken ct = default)
    {
        if (result.IsFailure)
        {
            await SendFailureAsync(httpContext, result, ct);
            return;
        }

        await Results.Ok(result.Value).ExecuteAsync(httpContext);
    }

    public static async Task ResolveAsync(
        HttpContext httpContext,
        Result result,
        CancellationToken ct = default)
    {
        if (result.IsFailure)
        {
            await SendFailureAsync(httpContext, result, ct);
            return;
        }

        await Results.Ok().ExecuteAsync(httpContext);
    }
}
