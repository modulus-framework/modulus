namespace Modulus.SignalR.Azure.Extensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class AzureBackplaneExtensions
{
    /// <summary>
    /// Configures SignalR to use Azure SignalR Service.
    /// Reads connection string from SignalR:Azure:ConnectionString.
    /// </summary>
    public static IServiceCollection AddAzureSignalRBackplane(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connStr = configuration["SignalR:Azure:ConnectionString"]
            ?? throw new InvalidOperationException(
                "SignalR:Azure:ConnectionString is not configured.");

        var applicationName = configuration["SignalR:Azure:ApplicationName"];

        var signalR = services.AddSignalR(opts => opts.EnableDetailedErrors = true)
            .AddAzureSignalR(o =>
            {
                o.ConnectionString = connStr;
                if (applicationName is not null)
                    o.ApplicationName = applicationName;
            });

        return services;
    }
}

public sealed class AzureBackplaneOptions
{
    public string ConnectionString { get; set; } = default!;
    public string? ApplicationName { get; set; }
    public int? ServerTimeoutSeconds { get; set; }
}
