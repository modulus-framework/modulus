namespace Modulus.Grpc;

using global::Grpc.Core;
using global::Grpc.Net.Client.Configuration;
using global::Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Correlation;
using Modulus.Core.Http;
using Modulus.Grpc.Client;

/// <summary>
/// gRPC clients hardened for service-to-service calls, the gRPC counterpart of <c>AddModulusHttpClient</c>. The
/// client comes from the HTTP client factory, so anything that works on an <see cref="IHttpClientBuilder"/> works on
/// it too (a BFF adds the caller's token with <c>.AddBffUserAccessToken()</c>; service discovery with
/// <c>.AddServiceDiscovery()</c> and a plain <c>http://catalog</c> address).
/// </summary>
public static class GrpcClientExtensions
{
    /// <summary>Registers <typeparamref name="TClient"/> (a <c>protoc</c>-generated client) against <paramref name="address"/>.</summary>
    public static IHttpClientBuilder AddModulusGrpcClient<TClient>(this IServiceCollection services, Uri address)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(address);
        return services.AddModulusGrpcClient<TClient>((_, options) => options.Address = address);
    }

    /// <summary>
    /// Registers <typeparamref name="TClient"/> (a <c>protoc</c>-generated client) with the defaults of
    /// <see cref="ModulusGrpcClientOptions"/> (<c>Grpc:Client</c>):
    /// <list type="bullet">
    /// <item>retries on <c>Unavailable</c> with jittered back-off (gRPC's own retry policy, so a call is never replayed
    /// after the server acted on it, streaming calls included);</item>
    /// <item>inside a gRPC service, the incoming call's deadline and cancellation are passed on;</item>
    /// <item>a default deadline for unary calls that have none;</item>
    /// <item>the correlation id (<c>X-Correlation-ID</c>) on every call.</item>
    /// </list>
    /// Add <see cref="PropagateTenant"/> and <see cref="ForwardAccessToken"/> when the callee needs the caller's tenant
    /// or token.
    /// </summary>
    public static IHttpClientBuilder AddModulusGrpcClient<TClient>(
        this IServiceCollection services,
        Action<IServiceProvider, GrpcClientFactoryOptions> configure)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.TryAddSingleton<ICorrelationContext, CorrelationContext>();
        if (!services.Any(d => d.ServiceType == typeof(GrpcClientDefaultsMarker)))
        {
            services.AddSingleton<GrpcClientDefaultsMarker>();
            services.AddOptions<ModulusGrpcClientOptions>().BindConfiguration(ModulusGrpcClientOptions.SectionName);
        }

        return services.AddGrpcClient<TClient>(configure)
            .ConfigureChannel((sp, channel) =>
            {
                var options = sp.GetRequiredService<IOptions<ModulusGrpcClientOptions>>().Value;
                channel.ServiceConfig ??= RetryOn(options);
            })
            // Keep-alive pings notice a connection a load balancer dropped silently; extra HTTP/2 connections stop one
            // connection's 100-stream limit from queueing calls. A later ConfigurePrimaryHttpMessageHandler replaces this.
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<ModulusGrpcClientOptions>>().Value;
                var handler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true };
                if (options.KeepAlivePingDelay is { } delay)
                {
                    handler.KeepAlivePingDelay = delay;
                    handler.KeepAlivePingTimeout = options.KeepAlivePingTimeout;
                    handler.KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests;
                }

                return handler;
            })
            // Order matters: the incoming call's deadline is applied first, the default only fills a gap.
            .EnableCallContextPropagation(o => o.SuppressContextNotFoundErrors = true)
            .AddInterceptor(sp => new DefaultDeadlineInterceptor(
                sp.GetRequiredService<IOptions<ModulusGrpcClientOptions>>().Value.DefaultDeadline))
            .AddHttpMessageHandler(sp => new CorrelationIdPropagationHandler(sp.GetRequiredService<ICorrelationContext>()));
    }

    /// <summary>
    /// Sends the current tenant (<see cref="ICurrentTenant"/>) as <paramref name="headerName"/>, never overwriting a
    /// value the caller set. The callee trusts that header only when its tenant resolver does (it should for calls from
    /// its own services, never for the public edge); with a forwarded user token, the token's tenant claim is enough.
    /// </summary>
    public static IHttpClientBuilder PropagateTenant(this IHttpClientBuilder builder, string headerName = "X-Tenant-Id")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(headerName);
        return builder.AddHttpMessageHandler(sp => new TenantPropagationHandler(sp.GetService<ICurrentTenant>(), headerName));
    }

    /// <summary>
    /// Forwards the incoming request's <c>Authorization</c> header (the caller's bearer token), so the callee
    /// authorizes the same user. Calls made outside a request, or that already carry a token, are left alone.
    /// </summary>
    public static IHttpClientBuilder ForwardAccessToken(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHttpContextAccessor();
        return builder.AddHttpMessageHandler(sp => new AccessTokenForwardingHandler(
            sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()));
    }

    internal static ServiceConfig? RetryOn(ModulusGrpcClientOptions options)
    {
        if (options.MaxAttempts <= 1)
            return null;

        return new ServiceConfig
        {
            MethodConfigs =
            {
                new MethodConfig
                {
                    Names = { MethodName.Default },
                    RetryPolicy = new RetryPolicy
                    {
                        // gRPC caps attempts at 5 (GrpcChannelOptions.MaxRetryAttempts).
                        MaxAttempts = Math.Min(options.MaxAttempts, 5),
                        InitialBackoff = options.InitialBackoff,
                        MaxBackoff = options.MaxBackoff,
                        BackoffMultiplier = 1.5,
                        RetryableStatusCodes = { StatusCode.Unavailable },
                    },
                },
            },
        };
    }
}

/// <summary>Marks that the client defaults were bound.</summary>
internal sealed class GrpcClientDefaultsMarker;
