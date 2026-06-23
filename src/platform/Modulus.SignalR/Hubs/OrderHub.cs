using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Modulus.SignalR.Abstractions;

namespace Modulus.SignalR.Hubs;

using Microsoft.AspNetCore.SignalR;
using Modulus.Core.Abstractions;

public interface IOrderClient
{
    Task OrderStatusChanged(Guid orderId, string status, DateTime updatedAt);
    Task DeliveryLocationUpdated(Guid orderId, double lat, double lng);
}

public sealed class OrderHub(ICurrentUser user)
    : Hub<IOrderClient>
{
    public override async Task OnConnectedAsync()
    {
        if (user.UserId.HasValue)
            await Groups.AddToGroupAsync(
                Context.ConnectionId, CustomerGroup(user.UserId.Value));
        await base.OnConnectedAsync();
    }

    public Task TrackOrder(Guid orderId)
        => Groups.AddToGroupAsync(
            Context.ConnectionId, OrderGroup(orderId));

    public Task StopTracking(Guid orderId)
        => Groups.RemoveFromGroupAsync(
            Context.ConnectionId, OrderGroup(orderId));

    public static string CustomerGroup(Guid id) => $"order:customer:{id}";
    public static string OrderGroup(Guid id)    => $"order:tracking:{id}";
}

internal sealed class OrderHubRegistrar : IModuleHub
{
    public void MapHub(IEndpointRouteBuilder app)
        => app.MapHub<OrderHub>("/hubs/orders");
}