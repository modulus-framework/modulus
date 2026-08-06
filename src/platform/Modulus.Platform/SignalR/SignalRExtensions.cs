using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Modulus.SignalR.Extensions;

using System.Reflection;
using Modulus.SignalR.Abstractions;

public static class SignalRExtensions
{
    public static IServiceCollection AddModuleHubs(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        services.AddScoped<IRealtimeBus, RealtimeBus>();

        // Auto-discover IModuleHub registrars
        foreach (var assembly in assemblies)
            foreach (var type in assembly.GetTypes()
                .Where(t => t is { IsAbstract: false, IsInterface: false }
                    && t.IsAssignableTo(typeof(IModuleHub))))
                services.AddTransient(typeof(IModuleHub), type);

        return services;
    }

    public static WebApplication MapModuleHubs(
        this WebApplication app)
    {
        var hubs = app.Services
            .GetRequiredService<IEnumerable<IModuleHub>>();
        foreach (var hub in hubs)
            hub.MapHub(app);
        return app;
    }

    /// <summary>
    /// Registers SignalR with production-safe defaults and returns the builder so
    /// a backplane can be chained. In-process only here; for a scale-out backplane
    /// add the <c>Modulus.SignalR.Backplane</c> package and chain
    /// <c>.AddRedisBackplane(config)</c> or <c>.AddAzureBackplane(config)</c> — that
    /// keeps the Redis / Azure SignalR SDKs out of <c>Modulus.Platform</c>.
    /// <code>
    /// services.AddModulusSignalR(config).AddRedisBackplane(config);
    /// </code>
    /// </summary>
    public static ISignalRServerBuilder AddModulusSignalR(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // EnableDetailedErrors ships full exception stack traces and source
        // information to clients — an information-disclosure risk in
        // production. Gate it on the Development environment only.
        var envName = configuration["Environment"]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        var enableDetailedErrors = envName is "Development" or "Dev";

        return services.AddSignalR(
            opts => opts.EnableDetailedErrors = enableDetailedErrors);
    }
}
