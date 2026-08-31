using TradeFlow.Modules.Notifications.Presentation.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace TradeFlow.Modules.Notifications.Presentation;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationHub(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<NotificationHub>("/hubs/notifications");
        return endpoints;
    }
}
