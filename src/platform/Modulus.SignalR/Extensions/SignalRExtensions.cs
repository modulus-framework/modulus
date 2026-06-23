using Microsoft.AspNetCore.Builder;
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

    public static IServiceCollection AddSignalRBackplane(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var backplane = configuration["SignalR:Backplane"];
        var signalR   = services
            .AddSignalR(opts => opts.EnableDetailedErrors = true);

        switch (backplane?.ToLowerInvariant())
        {
            case "redis":
                signalR.AddStackExchangeRedis(
                    configuration["SignalR:Redis:ConnectionString"]!);
                break;
            case "azure":
                signalR.AddAzureSignalR(
                    configuration["SignalR:Azure:ConnectionString"]!);
                break;
            // default: in-process, no backplane
        }

        return services;
    }
}