using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Modulus.AspNetCore.Http;

namespace Modulus.AspNetCore.Middleware;

using Microsoft.AspNetCore.Diagnostics;
using Modulus.Core.Abstractions;
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

        // A request the server refused to read (body too large, malformed) is the caller's fault, not a 500.
        if (exception is BadHttpRequestException bad)
        {
            logger.LogWarning("Handled client error: {Type}: {Message}", exception.GetType().Name, exception.Message);
            await ProblemResponses.WriteAsync(ctx, bad.StatusCode, "Bad request", "BAD_REQUEST");
            return true;
        }

        var error = ModulusErrorCatalog.Classify(exception);
        var status = error.Kind switch
        {
            ModulusErrorKind.Validation => StatusCodes.Status400BadRequest,
            ModulusErrorKind.NotFound or ModulusErrorKind.FeatureDisabled => StatusCodes.Status404NotFound,
            ModulusErrorKind.Unauthenticated => StatusCodes.Status401Unauthorized,
            ModulusErrorKind.PermissionDenied => StatusCodes.Status403Forbidden,
            ModulusErrorKind.Conflict or ModulusErrorKind.ConcurrencyConflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };

        // 4xx are client errors — log at Warning to avoid flooding alerting.
        // 5xx are genuine server faults — log at Error.
        if (error.IsClientError)
            logger.LogWarning("Handled client error: {Type}: {Message}",
                exception.GetType().Name, exception.Message);
        else
            logger.LogError(exception, "Unhandled exception: {Type}",
                exception.GetType().Name);

        if (exception is ValidationException ve)
        {
            // Same dictionary shape the endpoint validators answer with.
            await ProblemResponses.WriteValidationAsync(
                ctx, ve.FieldErrors.ToDictionary(e => e.Key, e => e.Value), error.Title, error.Code);
            return true;
        }

        Dictionary<string, object?>? extra = exception is FeatureDisabledException fe
            ? new() { ["feature"] = fe.Feature }
            : null;
        await ProblemResponses.WriteAsync(ctx, status, error.Title, error.Code, extra);

        return true;
    }
}
