using Microsoft.Extensions.DependencyInjection;

namespace Modulus.SignalR;

using Modulus.Events.Abstractions;
using Modulus.SignalR.Abstractions;

internal sealed class RealtimeBus(IServiceProvider sp)
    : IRealtimeBus
{
    public async Task PublishAsync<TEvent>(
        TEvent @event, CancellationToken ct)
        where TEvent : IIntegrationEvent
    {
        var mappings = sp
            .GetServices<IRealtimeEventMapping<TEvent>>();
        foreach (var m in mappings)
            await m.HandleAsync(@event, ct);
    }
}