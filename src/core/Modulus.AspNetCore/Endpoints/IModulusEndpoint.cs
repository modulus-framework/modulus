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
