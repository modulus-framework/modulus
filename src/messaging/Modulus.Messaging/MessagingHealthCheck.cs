using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Modulus.Messaging;

/// <summary>
/// Reports the messaging transport as unhealthy when its broker or database is unreachable, or when the check itself
/// fails or times out (FR-24). Register it with <c>AddModulusMessagingHealthCheck</c>.
/// </summary>
public sealed class MessagingHealthCheck(IMessageTransport transport) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (transport is not IMessageTransportHealth health)
        {
            return HealthCheckResult.Healthy("The transport does not report its connection state.");
        }

        try
        {
            return await health.IsAvailableAsync(cancellationToken).ConfigureAwait(false)
                ? HealthCheckResult.Healthy("The messaging transport is reachable.")
                : HealthCheckResult.Unhealthy("The messaging transport is unreachable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("The messaging transport check failed.", ex);
        }
    }
}

public static class MessagingHealthCheckExtensions
{
    /// <summary>Adds <see cref="MessagingHealthCheck"/> under <paramref name="name"/>, tagged <c>messaging</c>.</summary>
    public static IHealthChecksBuilder AddModulusMessagingHealthCheck(this IHealthChecksBuilder builder, string name = "messaging")
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<MessagingHealthCheck>();
        return builder.AddCheck<MessagingHealthCheck>(name, tags: ["messaging"]);
    }
}
