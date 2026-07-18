namespace Modulus.SignalR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Scale-out backplane extensions for SignalR. Isolated in their own package so
/// the <c>StackExchange.Redis</c> and <c>Microsoft.Azure.SignalR</c> SDKs are
/// pulled in only when an app runs multiple SignalR nodes — <c>Modulus.Platform</c>
/// keeps only the in-process hub support.
/// </summary>
public static class SignalRBackplaneExtensions
{
    /// <summary>
    /// Adds a Redis backplane using <c>SignalR:Redis:ConnectionString</c> so hub
    /// messages fan out across every server instance.
    /// </summary>
    public static ISignalRServerBuilder AddRedisBackplane(
        this ISignalRServerBuilder builder,
        IConfiguration configuration)
    {
        var connectionString = configuration["SignalR:Redis:ConnectionString"]
            ?? throw new InvalidOperationException(
                "SignalR:Redis:ConnectionString is required for the Redis backplane.");
        return builder.AddStackExchangeRedis(connectionString);
    }

    /// <summary>
    /// Offloads connections to Azure SignalR Service using
    /// <c>SignalR:Azure:ConnectionString</c>.
    /// </summary>
    public static ISignalRServerBuilder AddAzureBackplane(
        this ISignalRServerBuilder builder,
        IConfiguration configuration)
    {
        var connectionString = configuration["SignalR:Azure:ConnectionString"]
            ?? throw new InvalidOperationException(
                "SignalR:Azure:ConnectionString is required for Azure SignalR.");
        return builder.AddAzureSignalR(connectionString);
    }
}
