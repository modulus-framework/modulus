namespace Modulus.Grpc.Server;

using global::Grpc.Core;
using global::Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Maps what a service throws to a gRPC status through <see cref="GrpcExceptionMapper"/>, and logs it like the HTTP
/// exception handler does: client errors at Warning, server faults at Error. A call the client cancelled is left to
/// gRPC (it is not a server fault).
/// </summary>
internal sealed class GrpcExceptionInterceptor(
    ILogger<GrpcExceptionInterceptor> logger,
    IOptions<ModulusGrpcOptions> options,
    Modulus.Core.Abstractions.ICorrelationContext? correlation = null) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (Exception ex) when (ShouldMap(ex, context))
        {
            throw Translate(ex, context);
        }
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(requestStream, context);
        }
        catch (Exception ex) when (ShouldMap(ex, context))
        {
            throw Translate(ex, context);
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(request, responseStream, context);
        }
        catch (Exception ex) when (ShouldMap(ex, context))
        {
            throw Translate(ex, context);
        }
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(requestStream, responseStream, context);
        }
        catch (Exception ex) when (ShouldMap(ex, context))
        {
            throw Translate(ex, context);
        }
    }

    // An RpcException the service threw on purpose passes through untouched, and so does the cancellation of a call
    // the client abandoned.
    private static bool ShouldMap(Exception exception, ServerCallContext context)
        => exception is not RpcException
           && !(exception is OperationCanceledException && context.CancellationToken.IsCancellationRequested);

    private RpcException Translate(Exception exception, ServerCallContext context)
    {
        var (rpc, isClientError) = GrpcExceptionMapper.Map(
            exception, options.Value.EnableDetailedErrors,
            correlation?.CorrelationId ?? System.Diagnostics.Activity.Current?.TraceId.ToString());
        if (isClientError)
        {
            logger.LogWarning("gRPC {Method} answered {Status}: {Type}: {Message}",
                context.Method, rpc.StatusCode, exception.GetType().Name, exception.Message);
        }
        else
        {
            logger.LogError(exception, "gRPC {Method} failed: {Type}", context.Method, exception.GetType().Name);
        }

        return rpc;
    }
}
