using Microsoft.AspNetCore.SignalR;

namespace ProcureFlow.Modules.Notifications.Presentation.Hubs;

public sealed class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        string userId = Context.UserIdentifier ?? "anonymous";
        await Groups.AddToGroupAsync(Context.ConnectionId, $"User-{userId}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string userId = Context.UserIdentifier ?? "anonymous";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"User-{userId}");
        await base.OnDisconnectedAsync(exception);
    }

    public async Task SubscribeToNotifications()
    {
        string userId = Context.UserIdentifier ?? "anonymous";
        await Groups.AddToGroupAsync(Context.ConnectionId, $"User-{userId}");
    }

    public async Task UnsubscribeFromNotifications()
    {
        string userId = Context.UserIdentifier ?? "anonymous";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"User-{userId}");
    }
}
