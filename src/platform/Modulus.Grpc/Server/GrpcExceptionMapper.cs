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

    /// <summary>
    /// Maps <paramref name="exception"/> and, when <paramref name="requestId"/> is given, adds a
    /// <c>google.rpc.RequestInfo</c> detail with it, so a client can quote the id to support.
    /// </summary>
    public static RpcException ToRpcException(Exception exception, bool includeExceptionDetails, string? requestId)
        => Map(exception, includeExceptionDetails, requestId).Exception;

    internal static (RpcException Exception, bool IsClientError) Map(
        Exception exception, bool includeExceptionDetails, string? requestId = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is RpcException rpc)
            return (rpc, rpc.StatusCode != StatusCode.Internal && rpc.StatusCode != StatusCode.Unknown);

        var (mapped, isClientError) = MapCore(exception, includeExceptionDetails);
        return (requestId is null ? mapped : WithRequestInfo(mapped, requestId), isClientError);
    }

    private static RpcException WithRequestInfo(RpcException exception, string requestId)
    {
        var status = exception.GetRpcStatus();
        if (status is null)
            return exception;

        status.Details.Add(Any.Pack(new RequestInfo { RequestId = requestId }));
        return status.ToRpcException();
    }

    private static (RpcException Exception, bool IsClientError) MapCore(Exception exception, bool includeExceptionDetails)
    {
        var error = ModulusErrorCatalog.Classify(exception);
        return error.Kind switch
        {
            ModulusErrorKind.Validation => (Validation((ValidationException)exception), true),
            ModulusErrorKind.NotFound => (Build(StatusCode.NotFound, error.Title, error.Code), true),
            ModulusErrorKind.Unauthenticated => (Build(StatusCode.Unauthenticated, error.Title, error.Code), true),
            ModulusErrorKind.PermissionDenied => (Build(StatusCode.PermissionDenied, error.Title, error.Code), true),
            ModulusErrorKind.Conflict or ModulusErrorKind.ConcurrencyConflict
                => (Build(StatusCode.Aborted, error.Title, error.Code), true),
            ModulusErrorKind.FeatureDisabled => (Build(StatusCode.NotFound, error.Title, error.Code,
                new Dictionary<string, string> { ["feature"] = ((FeatureDisabledException)exception).Feature }), true),
            ModulusErrorKind.Cancelled => (Build(StatusCode.Cancelled, "The call was cancelled", error.Code), true),
            _ => (Build(StatusCode.Internal,
                includeExceptionDetails ? exception.ToString() : error.Title, error.Code), false),
        };
    }

    private static RpcException Validation(ValidationException exception)
    {
        var badRequest = new BadRequest();
        foreach (var (field, messages) in exception.FieldErrors)
        {
            foreach (var message in messages)
            {
                badRequest.FieldViolations.Add(new BadRequest.Types.FieldViolation
                {
                    // google.rpc field paths follow the proto field names (snake_case), not the C# property names.
                    Field = ToProtoFieldPath(field),
                    Description = message,
                });
            }
        }

        return Build(StatusCode.InvalidArgument, "Validation failed", "VALIDATION_FAILED", details: badRequest);
    }

    // "UnitPrice" -> "unit_price"; nested paths ("Address.PostalCode" -> "address.postal_code") convert per segment.
    internal static string ToProtoFieldPath(string property)
    {
        if (property.Length == 0)
            return property;

        var builder = new System.Text.StringBuilder(property.Length + 4);
        for (var i = 0; i < property.Length; i++)
        {
            var c = property[i];
            if (char.IsUpper(c))
            {
                var startsWord = i > 0 && property[i - 1] != '.' && property[i - 1] != '_'
                    && (!char.IsUpper(property[i - 1]) || (i + 1 < property.Length && char.IsLower(property[i + 1])));
                if (startsWord)
                    builder.Append('_');
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
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
}
