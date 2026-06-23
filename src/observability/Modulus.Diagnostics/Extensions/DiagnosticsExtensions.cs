using Microsoft.Extensions.DependencyInjection;
using Modulus.AspNetCore.Endpoints;
using Modulus.Core.Abstractions;
using Modulus.Diagnostics.Endpoints;

namespace Modulus.Diagnostics.Extensions;

using System.Reflection;

public static class DiagnosticsExtensions
{
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

        // Register health endpoints
        services.AddScoped<IEndpoint, ModuleHealthEndpoint>();
        services.AddScoped<IEndpoint, ModuleGraphEndpoint>();
        return services;
    }
}