namespace Modulus.Bff.Proxy;

using System.Net.Http.Headers;
using Microsoft.Extensions.Primitives;
using Modulus.Bff.Tokens;
using Modulus.Core.Abstractions;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

/// <summary>The registered clients, in registration order.</summary>
internal sealed class BffClientRegistry
{
    private readonly List<string> _names = [];

    public IReadOnlyList<string> Names => _names;

    public void Add(string name)
    {
        if (_names.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"BFF client '{name}' is registered twice.");
        _names.Add(name);
    }
}

/// <summary>
/// Builds YARP routes and clusters from <c>Bff:Clients:{name}:RemoteApis</c> and
/// <c>Bff:Services</c>: one cluster per upstream service (one <c>api</c> in a modular monolith,
/// one per service with microservices) and one route per client remote API, guarded by the
/// client's policy and rate limit.
/// </summary>
internal sealed class BffProxyConfigProvider(BffClientRegistry registry, IOptionsMonitor<BffClientOptions> clients, IOptions<BffOptions> bff)
    : IProxyConfigProvider
{
    public const string ClientMetadataKey = "bff.client";
    public const string EventStreamMetadataKey = "bff.event-stream";
    private const string CatchAll = "bff-path";
    private IProxyConfig? _config;

    public IProxyConfig GetConfig() => _config ??= Build();

    internal IProxyConfig Build()
    {
        var routes = new List<RouteConfig>();
        var services = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in registry.Names)
        {
            var options = clients.Get(name);
            var prefix = BffPaths.Normalize(options.PathPrefix);
            for (var i = 0; i < options.RemoteApis.Count; i++)
            {
                var api = options.RemoteApis[i];
                var local = BffPaths.Normalize(api.LocalPath);
                if (local.Length == 0)
                    throw new InvalidOperationException($"Bff:Clients:{name}:RemoteApis:{i}:LocalPath is required.");
                var remote = BffPaths.Normalize(api.RemotePath ?? api.LocalPath);
                services.Add(api.Service);

                routes.Add(new RouteConfig
                {
                    RouteId = $"bff-{name}-{i}",
                    ClusterId = ClusterId(api.Service),
                    Match = new RouteMatch { Path = $"{prefix}{local}/{{**{CatchAll}}}" },
                    AuthorizationPolicy = api.RequireAuthentication ? BffDefaults.Policy(name) : "anonymous",
                    RateLimiterPolicy = BffPaths.HasRateLimit(options) ? BffDefaults.RateLimitPolicy(name) : null,
                    Metadata = api.EventStream
                        ? new Dictionary<string, string> { [ClientMetadataKey] = name, [EventStreamMetadataKey] = "true" }
                        : new Dictionary<string, string> { [ClientMetadataKey] = name },
                    Transforms = [new Dictionary<string, string> { ["PathPattern"] = $"{remote}/{{**{CatchAll}}}" }],
                });
            }
        }

        var clusters = services.Select(service =>
        {
            var address = bff.Value.GetServiceAddress(service)
                ?? throw new InvalidOperationException($"Bff:Services:{service}:Address is not configured.");
            return new ClusterConfig
            {
                ClusterId = ClusterId(service),
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    [service] = new() { Address = address },
                },
            };
        }).ToList();

        return new StaticProxyConfig(routes, clusters);
    }

    private static string ClusterId(string service) => "bff-svc-" + service.ToLowerInvariant();

    private sealed class StaticProxyConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters) : IProxyConfig
    {
        public IReadOnlyList<RouteConfig> Routes { get; } = routes;

        public IReadOnlyList<ClusterConfig> Clusters { get; } = clusters;

        public IChangeToken ChangeToken { get; } = new CancellationChangeToken(CancellationToken.None);
    }
}

/// <summary>
/// Per-client request transforms for BFF routes: the cookie never leaves the BFF, the upstream
/// sees <c>X-Client-App</c> and the correlation id, and the web client's server-side access token
/// replaces whatever <c>Authorization</c> the browser sent (mobile/partner keep their own bearer).
/// </summary>
internal sealed class BffProxyTransformProvider(IOptionsMonitor<BffClientOptions> clients) : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context)
    {
    }

    public void ValidateCluster(TransformClusterValidationContext context)
    {
    }

    public void Apply(TransformBuilderContext context)
    {
        if (context.Route.Metadata?.TryGetValue(BffProxyConfigProvider.ClientMetadataKey, out var client) != true || client is null)
            return;

        context.AddRequestTransform(async transform =>
        {
            var request = transform.ProxyRequest;
            request.Headers.Remove("Cookie");
            request.Headers.Remove(BffDefaults.ClientAppHeader);
            request.Headers.TryAddWithoutValidation(BffDefaults.ClientAppHeader, client);

            var services = transform.HttpContext.RequestServices;
            var correlation = services.GetService<ICorrelationContext>();
            if (correlation?.CorrelationId is { } id && !request.Headers.Contains(CorrelationHeaders.Default))
                request.Headers.TryAddWithoutValidation(CorrelationHeaders.Default, id);

            if (clients.Get(client).Kind != BffClientKind.Web)
                return;

            request.Headers.Authorization = null;
            var token = await services.GetRequiredService<IBffAccessTokenService>()
                .GetAccessTokenAsync(transform.HttpContext, client, ct: transform.CancellationToken).ConfigureAwait(false);
            if (token is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        });
    }
}

internal static class BffPaths
{
    /// <summary><c>"web/"</c>, <c>"/web"</c> → <c>"/web"</c>; empty stays empty.</summary>
    public static string Normalize(string? path)
    {
        var trimmed = (path ?? string.Empty).Trim().Trim('/');
        return trimmed.Length == 0 ? string.Empty : "/" + trimmed;
    }

    public static bool HasRateLimit(BffClientOptions options) => options.RateLimit is { PermitLimit: > 0 };
}
