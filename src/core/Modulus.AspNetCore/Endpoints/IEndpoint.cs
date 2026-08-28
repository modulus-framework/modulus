using Microsoft.AspNetCore.Routing;

namespace Modulus.AspNetCore.Endpoints;

/// <summary>
/// Contract for the <b>minimal-API endpoint style</b>: implement this, register
/// with <c>services.AddEndpoints(typeof(...).Assembly)</c>, and map with
/// <c>app.MapEndpoints()</c>.
/// </summary>
/// <remarks>
/// Modulus supports two authoring styles for HTTP endpoints — pick per module
/// (or per endpoint) whichever fits:
/// <list type="bullet">
/// <item><b>Minimal API</b> (<see cref="IEndpoint"/>/<see cref="IMinimalEndpoint"/>):
/// full control over the route and handler lambda; best for small surfaces,
/// health probes, and SSE/websocket-adjacent routes.</item>
/// <item><b>REPR pattern</b> (<see cref="IModulusEndpoint"/>, mapped via
/// <c>app.MapModulusEndpoints()</c>): one class per endpoint with declarative
/// route/verb/auth metadata, request/response types, and built-in validation;
/// best for CRUD-style APIs.</item>
/// </list>
/// Both compile down to ordinary ASP.NET Core minimal-API routes.
/// </remarks>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}

/// <summary>
/// Minimal API style. Implement MapEndpoint with a lambda handler.
/// </summary>
public interface IMinimalEndpoint : IEndpoint { }
