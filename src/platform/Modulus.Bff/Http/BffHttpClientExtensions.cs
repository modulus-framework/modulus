namespace Modulus.Bff;

using Modulus.Bff.Http;
using Modulus.Platform.Http;

/// <summary>Typed clients for calling upstream services from BFF endpoints (aggregators).</summary>
public static class BffHttpClientExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TClient"/> against upstream <paramref name="service"/>
    /// (<c>Bff:Services:{service}:Address</c>; with <c>Bff:UseServiceDiscovery</c> the address may
    /// be logical, e.g. <c>https+http://catalog</c>). The client gets Modulus' resilient pipeline
    /// (retry, circuit breaker, timeouts, correlation id), the caller's access token and
    /// <c>X-Client-App</c>. In a modular monolith every module client points at <c>api</c>; with
    /// microservices each points at its own service.
    /// </summary>
    public static IHttpClientBuilder AddBffApiClient<TClient>(this IServiceCollection services, string service = BffOptions.DefaultService)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(service);

        var builder = services.AddModulusHttpClient<TClient>()
            .ConfigureHttpClient((sp, http) =>
            {
                var options = sp.GetRequiredService<IOptions<BffOptions>>().Value;
                var address = options.GetServiceAddress(service)
                    ?? throw new InvalidOperationException($"Bff:Services:{service}:Address is not configured.");
                http.BaseAddress = new Uri(address.EndsWith('/') ? address : address + "/");
            })
            .AddBffUserAccessToken();

        builder.AddServiceDiscoveryIfEnabled();
        return builder;
    }

    /// <summary>
    /// Adds the caller's access token, <c>X-Client-App</c> and the selected company (<see cref="BffOptions.TenantHeader"/>)
    /// to an existing typed client, which keeps its own base
    /// address: the web session's server-side token (refreshed before expiry, refreshed and replayed once on a
    /// <c>401</c>) or the mobile/partner caller's bearer. Outside BFF endpoints it uses the host's default client
    /// (<see cref="BffBuilder.SetDefaultClient"/>), so a Razor Pages host's page models call the API as the signed-in user.
    /// </summary>
    public static IHttpClientBuilder AddBffUserAccessToken(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddHttpMessageHandler<BffClientHeaderHandler>()
            .AddHttpMessageHandler<BffTenantHeaderHandler>()
            .AddHttpMessageHandler<UserAccessTokenHandler>();
    }

    internal static void AddServiceDiscoveryIfEnabled(this IHttpClientBuilder builder)
    {
        // Service discovery resolves logical addresses; it is a pass-through for real ones, so it
        // is only added when the app opted in (it registers its own resolver services).
        if (builder.Services.Any(d => d.ServiceType == typeof(BffServiceDiscoveryMarker)))
            builder.AddServiceDiscovery();
    }
}

/// <summary>Marks that <c>Bff:UseServiceDiscovery</c> was on at registration.</summary>
internal sealed class BffServiceDiscoveryMarker;

