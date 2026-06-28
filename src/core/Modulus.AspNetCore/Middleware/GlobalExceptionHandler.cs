using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Modulus.AspNetCore.Middleware;

using Microsoft.AspNetCore.Diagnostics;
using Modulus.Core.Abstractions.Exceptions;

internal sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext ctx,
        Exception exception,
        CancellationToken ct)
    {
        // A cancelled request (client disconnect) is not a server fault.
        // Let the request pipeline unwind without an error response.
        if (exception is OperationCanceledException)
            return false;

        // With `using System.ComponentModel.DataAnnotations;` removed, the
        // unqualified `ValidationException` binds unambiguously to the
        // framework's own Modulus.Core.Abstractions.Exceptions.ValidationException.
        // (Previously the DataAnnotations type shadowed it and framework
        // validation errors fell through to the 500 arm.)
        var (status, title, isClientError) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed", true),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found", true),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized", true),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden", true),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict", true),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", false),
        };

        // 4xx are client errors — log at Warning to avoid flooding alerting.
        // 5xx are genuine server faults — log at Error.
        if (isClientError)
            logger.LogWarning("Handled client error: {Type}: {Message}",
                exception.GetType().Name, exception.Message);
        else
            logger.LogError(exception, "Unhandled exception: {Type}",
                exception.GetType().Name);

        Dictionary<string, object?>? extensions = null;
        if (exception is ValidationException ve)
            extensions = new() { ["errors"] = ve.Errors };

        await Results.Problem(
                title: title,
                statusCode: status,
                extensions: extensions)
            .ExecuteAsync(ctx);

        return true;
    }
}
