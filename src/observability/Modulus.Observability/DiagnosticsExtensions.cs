using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Diagnostics.Endpoints;

namespace Modulus.Diagnostics.Extensions;

using System.Reflection;

public static class DiagnosticsExtensions
{
    /// <summary>
    /// Discovers <see cref="IModuleHealthCheck"/> implementations in the given
    /// assemblies and registers them for aggregation.
    /// </summary>
    /// <remarks>
    /// Pair with <see cref="MapModulusDiagnostics"/> to expose the aggregated
    /// module-health endpoint. This method intentionally does NOT register
    /// HTTP endpoints itself — mapping is explicit so hosts control their
    /// route table.
    /// </remarks>
    public static IServiceCollection AddModuleDiagnostics(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        // Discover non-generic IModuleHealthCheck implementations
        foreach (var assembly in assemblies)
            foreach (var type in assembly.GetTypes()
                .Where(t => t is { IsAbstract: false, IsInterface: false }
                    && !t.IsGenericTypeDefinition
                    && t.IsAssignableTo(typeof(IModuleHealthCheck))))
                services.AddScoped(typeof(IModuleHealthCheck), type);

        return services;
    }

    /// <summary>
    /// Maps the diagnostics endpoints:
    /// <c>GET /health/modules</c> — aggregates every registered
    /// <see cref="IModuleHealthCheck"/> (503 when any is unhealthy);
    /// <c>GET /health/graph</c> — the module dependency graph with
    /// initialisation order.
    /// </summary>
    public static IEndpointRouteBuilder MapModulusDiagnostics(
        this IEndpointRouteBuilder app)
    {
        new ModuleHealthEndpoint().MapEndpoint(app);
        new ModuleGraphEndpoint().MapEndpoint(app);
        return app;
    }
}
