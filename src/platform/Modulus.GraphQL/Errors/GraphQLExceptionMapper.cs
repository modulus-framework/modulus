namespace Modulus.GraphQL;

using global::GraphQL;
using global::GraphQL.Execution;
using global::GraphQL.Server.Transports.AspNetCore.Errors;
using Modulus.Core.Abstractions.Exceptions;

/// <summary>
/// Turns an exception thrown by a resolver into the GraphQL error the caller receives, the counterpart of the HTTP
/// problem details (same messages) and of the gRPC status codes (same <c>extensions.code</c> values):
/// <list type="table">
/// <item><term><see cref="ValidationException"/></term><description><c>VALIDATION_FAILED</c>, with <c>extensions.errors</c></description></item>
/// <item><term><see cref="NotFoundException"/></term><description><c>NOT_FOUND</c></description></item>
/// <item><term><see cref="UnauthorizedException"/></term><description><c>UNAUTHENTICATED</c></description></item>
/// <item><term><see cref="ForbiddenException"/></term><description><c>PERMISSION_DENIED</c></description></item>
/// <item><term><see cref="ConflictException"/>, EF Core's concurrency exception</term><description><c>CONFLICT</c>, <c>CONCURRENCY_CONFLICT</c></description></item>
/// <item><term><see cref="FeatureDisabledException"/></term><description><c>FEATURE_DISABLED</c>, with <c>extensions.feature</c></description></item>
/// <item><term><see cref="OperationCanceledException"/></term><description><c>CANCELLED</c></description></item>
/// <item><term>anything else</term><description><c>INTERNAL</c>, without the exception text unless details are exposed</description></item>
/// </list>
/// A field the caller may not read (a failed <c>AuthorizeWithPolicy</c>) reports <c>UNAUTHENTICATED</c> or
/// <c>PERMISSION_DENIED</c> the same way. The field's value is <c>null</c> and the other fields still resolve.
/// </summary>
public static class GraphQLExceptionMapper
{
    /// <summary>The error a resolver exception maps to.</summary>
    public sealed record MappedError(string Message, string Code, bool IsClientError, IReadOnlyDictionary<string, object?> Extensions);

    /// <summary>Maps <paramref name="exception"/>.</summary>
    public static MappedError Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var none = new Dictionary<string, object?>();
        return exception switch
        {
            ValidationException ve => new("Validation failed", "VALIDATION_FAILED", true, new Dictionary<string, object?> { ["errors"] = ve.Errors.ToList() }),
            NotFoundException => new("Resource not found", "NOT_FOUND", true, none),
            UnauthorizedException => new("Unauthorized", "UNAUTHENTICATED", true, none),
            ForbiddenException => new("Forbidden", "PERMISSION_DENIED", true, none),
            ConflictException => new("Conflict", "CONFLICT", true, none),
            FeatureDisabledException fe => new("Feature not available", "FEATURE_DISABLED", true, new Dictionary<string, object?> { ["feature"] = fe.Feature }),
            OperationCanceledException => new("The request was cancelled", "CANCELLED", true, none),
            _ when IsDbUpdateConcurrencyException(exception) => new("Concurrent update conflict", "CONCURRENCY_CONFLICT", true, none),
            _ => new("An unexpected error occurred", "INTERNAL", false, none),
        };
    }

    // Matches EF Core's DbUpdateConcurrencyException without referencing EF Core (as GlobalExceptionHandler does).
    private static bool IsDbUpdateConcurrencyException(Exception exception)
        => string.Equals(exception.GetType().FullName, "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException", StringComparison.Ordinal);
}

/// <summary>
/// Writes errors per <see cref="GraphQLExceptionMapper"/>: a resolver exception (GraphQL.NET wraps it in an
/// <see cref="UnhandledError"/>) gets the mapped message, code and extensions; an authorization failure gets
/// <c>UNAUTHENTICATED</c> / <c>PERMISSION_DENIED</c>. Errors GraphQL itself reports (syntax, validation, limits) keep
/// their own codes.
/// </summary>
internal sealed class ModulusErrorInfoProvider(ErrorInfoProviderOptions options) : ErrorInfoProvider(options)
{
    private readonly bool _exposeDetails = options.ExposeExceptionDetails;

    public override ErrorInfo GetInfo(ExecutionError executionError)
    {
        var info = base.GetInfo(executionError);
        switch (executionError)
        {
            case AccessDeniedError denied:
                SetCode(ref info, denied.PreferredStatusCode == System.Net.HttpStatusCode.Unauthorized ? "UNAUTHENTICATED" : "PERMISSION_DENIED");
                break;
            case UnhandledError { InnerException: { } inner } when inner is not ExecutionError:
                var mapped = GraphQLExceptionMapper.Map(inner);
                if (mapped.IsClientError || !_exposeDetails)
                    info.Message = mapped.Message;
                SetCode(ref info, mapped.Code);
                foreach (var (key, value) in mapped.Extensions)
                    info.Extensions![key] = value;
                if (!_exposeDetails)
                    info.Extensions!.Remove("data");
                break;
        }

        return info;
    }

    private static void SetCode(ref ErrorInfo info, string code)
    {
        info.Extensions ??= new Dictionary<string, object?>();
        info.Extensions["code"] = code;
        info.Extensions["codes"] = new[] { code };
    }
}
