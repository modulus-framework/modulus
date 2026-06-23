using System.ComponentModel.DataAnnotations;
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
        Exception   exception,
        CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            ValidationException ve  => (400, "Validation failed"),
            NotFoundException       => (404, "Resource not found"),
            UnauthorizedException   => (401, "Unauthorized"),
            ForbiddenException      => (403, "Forbidden"),
            ConflictException       => (409, "Conflict"),
            _                       => (500, "An unexpected error occurred"),
        };

        logger.LogError(exception,
            "Unhandled exception: {Type}", exception.GetType().Name);

        await Results.Problem(
            title: title, statusCode: status)
            .ExecuteAsync(ctx);

        return true;
    }
}