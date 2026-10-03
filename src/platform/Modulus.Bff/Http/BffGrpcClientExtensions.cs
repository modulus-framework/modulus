namespace Modulus.Bff;

using Modulus.Grpc;

/// <summary>gRPC clients for calling upstream services from BFF endpoints (aggregators).</summary>
public static class BffGrpcClientExtensions
{
    /// <summary>
    /// Registers the <c>protoc</c>-generated <typeparamref name="TClient"/> against upstream <paramref name="service"/>
    /// (<c>Bff:Services:{service}:GrpcAddress</c>, else its <c>Address</c>), the gRPC counterpart of
    /// <see cref="BffHttpClientExtensions.AddBffApiClient{TClient}"/>: Modulus' gRPC client defaults
    /// (<c>AddModulusGrpcClient</c>: retry on <c>Unavailable</c>, default deadline, correlation id), the caller's access
    /// token and <c>X-Client-App</c>. gRPC needs an <c>http</c> or <c>https</c> address, so a service-discovery address
    /// such as <c>https+http://catalog</c> is used as <c>https://catalog</c> (its first scheme) and resolved by service
    /// discovery when <c>Bff:UseServiceDiscovery</c> is on.
    /// </summary>
    public static IHttpClientBuilder AddBffGrpcClient<TClient>(this IServiceCollection services, string service = BffOptions.DefaultService)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrEmpty(service);

        var builder = services.AddModulusGrpcClient<TClient>((sp, options) =>
        {
            var address = sp.GetRequiredService<IOptions<BffOptions>>().Value.GetGrpcServiceAddress(service)
                ?? throw new InvalidOperationException($"Bff:Services:{service}:Address is not configured.");
            options.Address = GrpcAddress(address);
        }).AddBffUserAccessToken();

        builder.AddServiceDiscoveryIfEnabled();
        return builder;
    }

    /// <summary>The address gRPC dials: a <c>a+b://host</c> service-discovery address becomes <c>a://host</c>.</summary>
    internal static Uri GrpcAddress(string address)
    {
        var scheme = address.IndexOf("://", StringComparison.Ordinal);
        if (scheme > 0 && address.AsSpan(0, scheme).Contains('+'))
            address = address[..address.IndexOf('+', StringComparison.Ordinal)] + address[scheme..];
        return new Uri(address);
    }
}
