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
    public static WebApplication MapEndpoints(
        this WebApplication app,
        RouteGroupBuilder? routeGroup = null)
    {
        IEndpointRouteBuilder builder = routeGroup ?? (IEndpointRouteBuilder)app;

        var endpoints = app.Services
            .GetRequiredService<IEnumerable<IEndpoint>>();

        foreach (var endpoint in endpoints)
            endpoint.MapEndpoint(builder);

        return app;
    }
}