using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using ModulusSample.Shared.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Trace;
using ApplicationException = ModulusSample.Shared.Application.Exceptions.ApplicationException;

namespace ModulusSample.Api.Middleware;

internal sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IWebHostEnvironment env,
    IProblemDetailsService problemDetailsService)
    : IExceptionHandler
{
    private const string GenericProductionDetail =
        "An unexpected error occurred while processing your request. Please try again later.";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogDebug(exception,
                "Request aborted by client: {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
            return true;
        }

        if (httpContext.Response.HasStarted)
        {
            logger.LogWarning(exception,
                "Response already started; cannot write ProblemDetails for {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
            return false;
        }

        ExceptionMapping mapping = MapException(exception);

        // Mark the OpenTelemetry span as failed and record the exception so failed
        // requests surface as errors in the trace backend (instead of staying "OK").
        RecordSpanFailure(exception, mapping.Status);

        LogException(exception, mapping.Status, httpContext);

        ProblemDetails problemDetails = BuildProblemDetails(httpContext, exception, mapping);

        httpContext.Response.StatusCode = mapping.Status;
        httpContext.Response.ContentType = "application/problem+json";

        bool written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });

        if (!written)
        {
            await httpContext.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: "application/problem+json",
                cancellationToken);
        }

        return true;
    }

    private ProblemDetails BuildProblemDetails(
        HttpContext httpContext,
        Exception exception,
        ExceptionMapping mapping)
    {
        bool isDevelopment = env.IsDevelopment();

        string? detail = isDevelopment
            ? mapping.DevelopmentDetail ?? exception.Message
            : mapping.ProductionDetail;

        var problem = new ProblemDetails
        {
            Status = mapping.Status,
            Type = mapping.Type,
            Title = mapping.Title,
            Detail = detail,
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
        };

        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        problem.Extensions["requestId"] = httpContext.TraceIdentifier;
        problem.Extensions["timestamp"] = DateTimeOffset.UtcNow;

        if (mapping.Extensions is not null)
        {
            foreach ((string key, object? value) in mapping.Extensions)
            {
                problem.Extensions[key] = value;
            }
        }

        if (isDevelopment)
        {
            problem.Extensions["exception"] = new
            {
                type = exception.GetType().FullName,
                source = exception.Source,
                stackTrace = exception.StackTrace,
                innerException = exception.InnerException?.Message
            };
        }

        return problem;
    }

    private void LogException(Exception exception, int status, HttpContext httpContext)
    {
        LogLevel level = status >= StatusCodes.Status500InternalServerError
            ? LogLevel.Error
            : LogLevel.Warning;

        logger.Log(level, exception,
            "Unhandled {ExceptionType} handling {Method} {Path} → {StatusCode}",
            exception.GetType().Name,
            httpContext.Request.Method,
            httpContext.Request.Path,
            status);
    }

    /// <summary>
    /// Records the failure on the active OpenTelemetry span: sets the span status to
    /// <see cref="ActivityStatusCode.Error"/>, tags the mapped HTTP status and records
    /// the exception as a span event. Only 5xx (and timeouts) are marked as errors;
    /// 4xx are left unset to avoid polluting error-based SLOs with client mistakes.
    /// </summary>
    private static void RecordSpanFailure(Exception exception, int status)
    {
        Activity? activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        activity.SetTag("error.type", exception.GetType().Name);
        activity.SetTag("http.response.status_code", status);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.AddException(exception);
        }
    }

    private static ExceptionMapping MapException(Exception exception) => exception switch
    {
        ValidationException validation => new ExceptionMapping(
            StatusCodes.Status400BadRequest,
            "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            "Validation failed",
            DevelopmentDetail: "One or more validation errors occurred.",
            ProductionDetail: "One or more validation errors occurred.",
            Extensions: new Dictionary<string, object?>
            {
                ["errors"] = validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray())
            }),

        UnauthorizedAccessException => new ExceptionMapping(
            StatusCodes.Status401Unauthorized,
            "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            "Unauthorized",
            ProductionDetail: "Authentication is required to access this resource."),

        InvalidOperationException opEx
            when opEx.Message.Contains("Required parameter", StringComparison.OrdinalIgnoreCase)
            => new ExceptionMapping(
                StatusCodes.Status400BadRequest,
                "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                "Missing required parameter",
                DevelopmentDetail: opEx.Message,
                ProductionDetail: ExtractRequiredParameterMessage(opEx.Message)),

        InvalidOperationException bindEx
            when bindEx.Message.Contains("Failed to bind parameter", StringComparison.OrdinalIgnoreCase)
            => new ExceptionMapping(
                StatusCodes.Status400BadRequest,
                "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                "Invalid parameter value",
                DevelopmentDetail: bindEx.Message,
                ProductionDetail: ExtractFailedToBindMessage(bindEx.Message)),

        KeyNotFoundException or InvalidOperationException
            when exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            => new ExceptionMapping(
                StatusCodes.Status404NotFound,
                "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                "Resource not found",
                ProductionDetail: "The requested resource was not found."),

        ArgumentException or ArgumentNullException => new ExceptionMapping(
            StatusCodes.Status400BadRequest,
            "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            "Bad request",
            ProductionDetail: "The request contains invalid arguments."),

        DbUpdateConcurrencyException or ConcurrencyException => new ExceptionMapping(
            StatusCodes.Status409Conflict,
            "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            "Concurrency conflict",
            ProductionDetail: "The resource was modified by another request. Please reload and retry."),

        DbUpdateException { InnerException: PostgresException { SqlState: "23505" } pgEx } => new ExceptionMapping(
            StatusCodes.Status409Conflict,
            "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            "Duplicate resource",
            DevelopmentDetail: $"Unique constraint '{pgEx.ConstraintName}' violated: {pgEx.MessageText}",
            ProductionDetail: "A resource with the same unique value already exists."),

        DbUpdateException { InnerException: PostgresException { SqlState: "23503" } } => new ExceptionMapping(
            StatusCodes.Status409Conflict,
            "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            "Referential integrity violation",
            ProductionDetail: "The operation references a resource that does not exist or is in use."),

        DbUpdateException { InnerException: PostgresException { SqlState: "23502" } } => new ExceptionMapping(
            StatusCodes.Status400BadRequest,
            "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            "Missing required field",
            ProductionDetail: "A required field was not provided."),

        TimeoutException => new ExceptionMapping(
            StatusCodes.Status504GatewayTimeout,
            "https://tools.ietf.org/html/rfc9110#section-15.6.5",
            "Gateway timeout",
            ProductionDetail: "The request timed out. Please try again."),

        NotImplementedException => new ExceptionMapping(
            StatusCodes.Status501NotImplemented,
            "https://tools.ietf.org/html/rfc9110#section-15.6.2",
            "Not implemented",
            ProductionDetail: "This feature is not yet available."),

        ApplicationException appEx => MapApplicationException(appEx),

        _ => new ExceptionMapping(
            StatusCodes.Status500InternalServerError,
            "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            "Internal server error",
            ProductionDetail: GenericProductionDetail)
    };

    private static ExceptionMapping MapApplicationException(ApplicationException appEx)
    {
        string? customMessage = appEx.Message != "Application exception"
            ? appEx.Message
            : null;

        if (appEx.Error is { } err && err.Code is not null)
        {
            return new ExceptionMapping(
                StatusCodes.Status400BadRequest,
                "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                "Application error",
                DevelopmentDetail: err.Message,
                ProductionDetail: customMessage ?? err.Message,
                Extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = err.Code,
                    ["requestName"] = appEx.RequestName
                });
        }

        return new ExceptionMapping(
            StatusCodes.Status500InternalServerError,
            "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            "Application error",
            DevelopmentDetail: appEx.Error?.Message ?? appEx.InnerException?.Message ?? appEx.Message,
            ProductionDetail: customMessage ?? GenericProductionDetail,
            Extensions: appEx.Error is { }
                ? new Dictionary<string, object?>
                {
                    ["errorCode"] = appEx.Error.Code,
                    ["requestName"] = appEx.RequestName
                }
                : null);
    }

    private sealed record ExceptionMapping(
        int Status,
        string Type,
        string Title,
        string? DevelopmentDetail = null,
        string? ProductionDetail = null,
        IReadOnlyDictionary<string, object?>? Extensions = null);

    private static string ExtractRequiredParameterMessage(string message)
    {
        const string search = "Required parameter ";
        int start = message.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return "A required query parameter was not provided.";
        }

        start += search.Length;
        int end = message.IndexOf(" was not provided", start, StringComparison.OrdinalIgnoreCase);
        if (end <= start)
        {
            return "A required query parameter was not provided.";
        }

        string paramName = message[start..end];
        return $"Required query parameter '{paramName}' was not provided.";
    }

    private static string ExtractFailedToBindMessage(string message)
    {
        const string search = "Failed to bind parameter ";
        int start = message.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return "Invalid parameter value provided.";
        }

        start += search.Length;
        int end = message.IndexOf(" from \"", start, StringComparison.OrdinalIgnoreCase);
        if (end <= start)
        {
            return "Invalid parameter value provided.";
        }

        string paramName = message[start..end];
        int valueStart = end + 7;
        int valueEnd = message.IndexOf("\"", valueStart, StringComparison.OrdinalIgnoreCase);
        string providedValue = valueEnd > valueStart
            ? message[valueStart..valueEnd]
            : "(empty)";

        return $"Invalid value '{providedValue}' for parameter '{paramName}'.";
    }
}
