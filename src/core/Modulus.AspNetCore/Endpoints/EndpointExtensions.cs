using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.AspNetCore.Endpoints;

using System.Reflection;

public static class EndpointExtensions
{
    /// <summary>
    /// Scans assemblies for IEndpoint implementations and registers as transient.
    /// Call from each module's ConfigureServices.
    /// </summary>
    public static IServiceCollection AddEndpoints(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var types = assembly.GetTypes()
                .Where(t => t is { IsAbstract: false, IsInterface: false }
                         && t.IsAssignableTo(typeof(IEndpoint)));

            foreach (var type in types)
                services.AddTransient(typeof(IEndpoint), type);
        }
        return services;
    }

    /// <summary>
    /// Maps all registered IEndpoint instances into the route table.
    /// Call once in Program.cs after app.Build().
    /// </summary>
    /// <remarks>
    /// Resolved inside a scope: endpoint implementations are transient and
    /// may (transitively) depend on scoped services — resolving them from the
    /// root provider would throw under scope validation or pin root-scope
    /// instances. Endpoint implementations must still resolve per-request
    /// dependencies from <c>HttpContext.RequestServices</c> inside their
    /// handlers rather than capturing scoped services at construction time.
    /// </remarks>
    public static WebApplication MapEndpoints(
        this WebApplication app,
        RouteGroupBuilder? routeGroup = null)
    {
        IEndpointRouteBuilder builder = routeGroup ?? (IEndpointRouteBuilder)app;

        using var scope = app.Services.CreateScope();
        var endpoints = scope.ServiceProvider
            .GetRequiredService<IEnumerable<IEndpoint>>();

        foreach (var endpoint in endpoints)
            endpoint.MapEndpoint(builder);

        return app;
    }
}
