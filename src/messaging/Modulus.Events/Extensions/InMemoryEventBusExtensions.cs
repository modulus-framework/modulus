namespace Modulus.Events.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Modulus.Events.Abstractions;

/// <summary>
/// Explicit in-memory event-bus registration.
/// Ensures <see cref="IModuleBus"/> is bound to <see cref="InProcessModuleBus"/>,
/// replacing any previously-registered broker implementation.
/// </summary>
public static class InMemoryEventBusExtensions
{
    public static IServiceCollection AddInMemoryEventBus(
        this IServiceCollection services)
    {
        services.RemoveIModuleBusRegistrations();
        services.AddScoped<IModuleBus, InProcessModuleBus>();
        return services;
    }
}
