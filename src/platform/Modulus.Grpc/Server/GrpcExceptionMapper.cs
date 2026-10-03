namespace Modulus.Grpc;

using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using global::Grpc.Core;
using Modulus.Core.Abstractions.Exceptions;
using RpcStatus = Google.Rpc.Status;

/// <summary>
/// Turns an exception thrown by a gRPC service into the <see cref="RpcException"/> the caller receives, the gRPC
/// counterpart of the HTTP problem details the API answers with:
/// <list type="table">
/// <item><term><see cref="ValidationException"/></term><description><c>InvalidArgument</c> with a <c>google.rpc.BadRequest</c> detail (one field violation per error)</description></item>
/// <item><term><see cref="NotFoundException"/></term><description><c>NotFound</c></description></item>
/// <item><term><see cref="UnauthorizedException"/></term><description><c>Unauthenticated</c></description></item>
/// <item><term><see cref="ForbiddenException"/></term><description><c>PermissionDenied</c></description></item>
/// <item><term><see cref="ConflictException"/>, EF Core's concurrency exception</term><description><c>Aborted</c></description></item>
/// <item><term><see cref="FeatureDisabledException"/></term><description><c>NotFound</c> with a <c>google.rpc.ErrorInfo</c> naming the feature</description></item>
/// <item><term><see cref="OperationCanceledException"/></term><description><c>Cancelled</c></description></item>
/// <item><term>anything else</term><description><c>Internal</c>, without the exception text unless detailed errors are on</description></item>
/// </list>
/// Every mapped status carries a <c>google.rpc.ErrorInfo</c> (<c>domain</c> = <c>modulus</c>), so clients can branch on
/// <see cref="ModulusRpcExceptionExtensions.GetErrorReason"/> rather than on message text.
/// </summary>
public static class GrpcExceptionMapper
{
    /// <summary>The <c>ErrorInfo.domain</c> of every status mapped here.</summary>
    public const string ErrorDomain = "modulus";

    /// <summary>Maps <paramref name="exception"/>; an <see cref="RpcException"/> is returned unchanged.</summary>
    public static RpcException ToRpcException(Exception exception, bool includeExceptionDetails = false)
        => Map(exception, includeExceptionDetails).Exception;

    internal static (RpcException Exception, bool IsClientError) Map(Exception exception, bool includeExceptionDetails)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is RpcException rpc)
            return (rpc, rpc.StatusCode != StatusCode.Internal && rpc.StatusCode != StatusCode.Unknown);

        return exception switch
        {
            ValidationException ve => (Validation(ve), true),
            NotFoundException => (Build(StatusCode.NotFound, "Resource not found", "NOT_FOUND"), true),
            UnauthorizedException => (Build(StatusCode.Unauthenticated, "Unauthorized", "UNAUTHENTICATED"), true),
            ForbiddenException => (Build(StatusCode.PermissionDenied, "Forbidden", "PERMISSION_DENIED"), true),
            ConflictException => (Build(StatusCode.Aborted, "Conflict", "CONFLICT"), true),
            FeatureDisabledException fe => (Build(StatusCode.NotFound, "Feature not available", "FEATURE_DISABLED",
                new Dictionary<string, string> { ["feature"] = fe.Feature }), true),
            OperationCanceledException => (Build(StatusCode.Cancelled, "The call was cancelled", "CANCELLED"), true),
            _ when IsDbUpdateConcurrencyException(exception)
                => (Build(StatusCode.Aborted, "Concurrent update conflict", "CONCURRENCY_CONFLICT"), true),
            _ => (Build(StatusCode.Internal,
                includeExceptionDetails ? exception.ToString() : "An unexpected error occurred", "INTERNAL"), false),
        };
    }

    private static RpcException Validation(ValidationException exception)
    {
        var badRequest = new BadRequest();
        foreach (var error in exception.Errors)
        {
            // The mediator's validation behaviour writes "Property: message"; anything else has no field.
            var separator = error.IndexOf(": ", StringComparison.Ordinal);
            var field = separator > 0 && !error[..separator].Contains(' ', StringComparison.Ordinal) ? error[..separator] : "";
            badRequest.FieldViolations.Add(new BadRequest.Types.FieldViolation
            {
                Field = field,
                Description = field.Length > 0 ? error[(separator + 2)..] : error,
            });
        }

        return Build(StatusCode.InvalidArgument, "Validation failed", "VALIDATION_FAILED", details: badRequest);
    }

    private static RpcException Build(
        StatusCode code,
        string message,
        string reason,
        IDictionary<string, string>? metadata = null,
        Google.Protobuf.IMessage? details = null)
    {
        var info = new ErrorInfo { Reason = reason, Domain = ErrorDomain };
        if (metadata is not null)
            info.Metadata.Add(metadata);

        var status = new RpcStatus { Code = (int)code, Message = message };
        status.Details.Add(Any.Pack(info));
        if (details is not null)
            status.Details.Add(Any.Pack(details));
        return status.ToRpcException();
    }

    // Matches EF Core's DbUpdateConcurrencyException without referencing EF Core (as GlobalExceptionHandler does).
    private static bool IsDbUpdateConcurrencyException(Exception exception)
        => string.Equals(exception.GetType().FullName, "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException", StringComparison.Ordinal);
}
