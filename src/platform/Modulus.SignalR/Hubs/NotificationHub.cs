using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Modulus.SignalR.Abstractions;

namespace Modulus.SignalR.Hubs;

using Microsoft.AspNetCore.SignalR;
using Modulus.Core.Abstractions;

public interface INotificationClient
{
    Task ReceiveNotification(NotificationDto notification);
    Task NotificationRead(Guid notificationId);
    Task UnreadCountChanged(int count);
}

public sealed record NotificationDto(
    Guid     Id,
    string   Channel,
    string   Content,
    bool     IsRead,
    DateTime CreatedAt);

public sealed class NotificationHub(
    ICurrentUser currentUser)
    : Hub<INotificationClient>
{
    public override async Task OnConnectedAsync()
    {
        if (currentUser.UserId.HasValue)
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                UserGroup(currentUser.UserId.Value));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? ex)
    {
        if (currentUser.UserId.HasValue)
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                UserGroup(currentUser.UserId.Value));
        await base.OnDisconnectedAsync(ex);
    }

    public static string UserGroup(Guid userId)
        => $"notification:user:{userId}";
}

internal sealed class NotificationHubRegistrar : IModuleHub
{
    public void MapHub(IEndpointRouteBuilder app)
        => app.MapHub<NotificationHub>("/hubs/notifications");
}