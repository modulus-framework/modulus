namespace Modulus.AspNetCore.Endpoints;

/// <summary>
/// Marker interface implemented by all REPR-style endpoints.
/// Used by <see cref="EndpointDiscovery"/> for assembly scanning.
/// </summary>
public interface IModulusEndpoint
{
    /// <summary>
    /// Called once at startup to configure the route, verb,
    /// version, permissions, and other metadata.
    /// </summary>
    void Configure();
}

/// <summary>
/// The typed handler contract of a REPR endpoint. Implemented by
/// <see cref="Endpoint{TRequest, TResponse}"/> and <see cref="Endpoint{TRequest}"/>;
/// <see cref="EndpointDiscovery"/> dispatches through it with a statically-typed
/// call closed per endpoint at startup — no per-request reflection.
/// </summary>
public interface IEndpointHandler<in TRequest>
    where TRequest : class
{
    /// <summary>Handles the bound, validated request.</summary>
    Task HandleAsync(TRequest req, CancellationToken ct);
}

/// <summary>
/// Internal metadata captured from <see cref="IModulusEndpoint.Configure"/>.
/// Read at registration time to build minimal-API route entries.
/// </summary>
internal sealed class EndpointConfig
{
    public string Route { get; private set; } = "/";
    public string Verb { get; private set; } = "GET";

    public int[] Versions { get; set; } = [1];
    public string[] Permissions { get; set; } = [];
    public string[] Roles { get; set; } = [];
    public string[] Policies { get; set; } = [];
    public bool AllowAnonymous { get; set; }
    public string? Tag { get; set; }
    public string? Summary { get; set; }
    public bool Deprecated { get; set; }
    public bool WrapResponse { get; set; } = true;

    public Type RequestType { get; set; } = null!;
    public Type? ResponseType { get; set; }

    internal void SetMethod(string verb, string route)
    {
        Verb = verb;
        Route = route;
    }
}
