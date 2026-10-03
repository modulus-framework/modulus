namespace Modulus.Grpc;

using Google.Rpc;
using global::Grpc.Core;

/// <summary>Reads the error details a Modulus gRPC service puts on a failed call (<see cref="GrpcExceptionMapper"/>).</summary>
public static class ModulusRpcExceptionExtensions
{
    /// <summary>
    /// The validation errors of an <c>InvalidArgument</c> status, as <c>"Field: message"</c> (or just the message when
    /// it names no field), the same text the HTTP API puts in <c>errors</c>. Empty when there are none.
    /// </summary>
    public static IReadOnlyList<string> GetValidationErrors(this RpcException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var badRequest = exception.GetRpcStatus()?.GetDetail<BadRequest>();
        if (badRequest is null)
            return [];
        return badRequest.FieldViolations
            .Select(v => string.IsNullOrEmpty(v.Field) ? v.Description : $"{v.Field}: {v.Description}")
            .ToList();
    }

    /// <summary>The <c>google.rpc.ErrorInfo</c> reason (e.g. <c>VALIDATION_FAILED</c>, <c>FEATURE_DISABLED</c>), or null.</summary>
    public static string? GetErrorReason(this RpcException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.GetRpcStatus()?.GetDetail<ErrorInfo>()?.Reason;
    }

    /// <summary>The <c>google.rpc.ErrorInfo</c> metadata (e.g. <c>feature</c>), empty when there is none.</summary>
    public static IReadOnlyDictionary<string, string> GetErrorMetadata(this RpcException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var info = exception.GetRpcStatus()?.GetDetail<ErrorInfo>();
        return info is null ? new Dictionary<string, string>() : new Dictionary<string, string>(info.Metadata);
    }
}
