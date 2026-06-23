namespace Modulus.Events.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modulus.Events.Abstractions;

/// <summary>
/// Shared helper for event-bus provider extensions to replace the default
/// <see cref="IModuleBus"/> registration cleanly.
/// </summary>
public static class EventBusRegistrationHelper
{
    /// <summary>Removes every existing <see cref="IModuleBus"/> service descriptor.</summary>
    public static void RemoveIModuleBusRegistrations(
        this IServiceCollection services)
    {
        var existing = services
            .Where(d => d.ServiceType == typeof(IModuleBus))
            .ToList();

        foreach (var descriptor in existing)
            services.Remove(descriptor);
    }
}
