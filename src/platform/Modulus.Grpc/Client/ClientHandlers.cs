namespace Modulus.Grpc.Client;

using System.Globalization;
using global::Grpc.Core;
using global::Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Modulus.Core.Abstractions;

/// <summary>Gives a unary call without a deadline the configured default.</summary>
internal sealed class DefaultDeadlineInterceptor(TimeSpan? deadline) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, WithDeadline(context));

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context, BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
        => continuation(request, WithDeadline(context));

    private ClientInterceptorContext<TRequest, TResponse> WithDeadline<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        if (deadline is not { } timeout || context.Options.Deadline is not null)
            return context;
        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host, context.Options.WithDeadline(DateTime.UtcNow.Add(timeout)));
    }
}

/// <summary>Sends the ambient tenant id.</summary>
internal sealed class TenantPropagationHandler(ICurrentTenant? tenant, string headerName) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (tenant?.TenantId is { } id && !request.Headers.Contains(headerName))
            request.Headers.TryAddWithoutValidation(headerName, id.ToString("D", CultureInfo.InvariantCulture));
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Copies the incoming request's <c>Authorization</c> header.</summary>
internal sealed class AccessTokenForwardingHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null
            && accessor.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString() is { Length: > 0 } authorization)
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, authorization);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
