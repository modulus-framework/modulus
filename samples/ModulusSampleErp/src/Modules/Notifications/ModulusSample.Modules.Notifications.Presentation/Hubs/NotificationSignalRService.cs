using Microsoft.AspNetCore.SignalR;
using ModulusSample.Modules.Notifications.Application.Notifications.Dtos;

namespace ModulusSample.Modules.Notifications.Presentation.Hubs;

public interface INotificationSignalRService
{
    Task SendNotificationAsync(Guid userId, NotificationResponse notification);
    Task SendNotificationToGroupAsync(string groupName, NotificationResponse notification);
    Task SendUnreadCountUpdateAsync(Guid userId, int count);
    Task BroadcastNotificationAsync(NotificationResponse notification);
}

public class NotificationSignalRService : INotificationSignalRService
{
    private readonly IHubContext<NotificationHub> _hubContext;

    public NotificationSignalRService(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task SendNotificationAsync(Guid userId, NotificationResponse notification)
    {
        await _hubContext.Clients.Group($"User-{userId}").SendAsync("ReceiveNotification", notification);
    }

    public async Task SendNotificationToGroupAsync(string groupName, NotificationResponse notification)
    {
        await _hubContext.Clients.Group(groupName).SendAsync("ReceiveNotification", notification);
    }

    public async Task SendUnreadCountUpdateAsync(Guid userId, int count)
    {
        await _hubContext.Clients.Group($"User-{userId}").SendAsync("UpdateUnreadCount", count);
    }

    public async Task BroadcastNotificationAsync(NotificationResponse notification)
    {
        await _hubContext.Clients.All.SendAsync("ReceiveNotification", notification);
    }
}
