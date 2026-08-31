namespace Modulus.Platform.Http;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Core.Abstractions;
using Modulus.Core.Correlation;
using Modulus.Core.Http;

/// <summary>
/// Registers outbound <see cref="System.Net.Http.HttpClient"/> instances hardened
/// for service-to-service calls: the .NET <b>standard resilience pipeline</b>
/// (retry with jittered back-off, circuit breaker, total-request timeout,
/// per-attempt timeout, and a concurrency limiter) plus automatic correlation-id
/// propagation. W3C <c>traceparent</c> is already forwarded by
/// <see cref="System.Net.Http.HttpClient"/> when an activity is current, so
/// distributed traces link up without extra wiring.
/// </summary>
public static class HttpClientExtensions
{
    /// <summary>
    /// Adds a named resilient client. Configure the base address / default headers
    /// through <paramref name="configureClient"/>. Chain
    /// <c>.ConfigureAdditionalHttpMessageHandlers</c> or reconfigure resilience on
    /// the returned <see cref="IHttpClientBuilder"/> as needed.
    /// </summary>
    public static IHttpClientBuilder AddModulusHttpClient(
        this IServiceCollection services,
        string name,
        Action<HttpClient>? configureClient = null)
    {
        RegisterCorrelationDependencies(services);

        var builder = configureClient is null
            ? services.AddHttpClient(name)
            : services.AddHttpClient(name, configureClient);

        return Harden(builder);
    }

    /// <summary>
    /// Adds a typed resilient client (<typeparamref name="TClient"/>). The
    /// implementation receives the hardened <see cref="HttpClient"/> via
    /// constructor injection.
    /// </summary>
    public static IHttpClientBuilder AddModulusHttpClient<TClient>(
        this IServiceCollection services,
        Action<HttpClient>? configureClient = null)
        where TClient : class
    {
        RegisterCorrelationDependencies(services);

        var builder = configureClient is null
            ? services.AddHttpClient<TClient>()
            : services.AddHttpClient<TClient>(configureClient);

        return Harden(builder);
    }

    // Correlation propagation is added as the OUTER handler so the id is stamped
    // once, then the standard resilience handler retries/short-circuits beneath it.
    private static IHttpClientBuilder Harden(IHttpClientBuilder builder)
    {
        builder.AddHttpMessageHandler(sp =>
            new CorrelationIdPropagationHandler(
                sp.GetRequiredService<ICorrelationContext>()));
        builder.AddStandardResilienceHandler();
        return builder;
    }

    // Ensure correlation and causation contexts exist even if AddModulusCorrelation
    // (the ASP.NET inbound side) was not called — the handlers then simply no-op.
    private static void RegisterCorrelationDependencies(IServiceCollection services)
    {
        services.TryAddSingleton<ICorrelationContext, CorrelationContext>();
        services.TryAddSingleton<ICausationIdContext, CausationIdContext>();
    }
}
