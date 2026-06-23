using Microsoft.AspNetCore.Routing;

namespace Modulus.AspNetCore.Endpoints;

/// <summary>
/// Common contract for all endpoint styles.
/// Auto-discovered by AddEndpoints() / MapEndpoints().
/// </summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}

/// <summary>
/// Minimal API style. Implement MapEndpoint with a lambda handler.
/// </summary>
public interface IMinimalEndpoint : IEndpoint { }