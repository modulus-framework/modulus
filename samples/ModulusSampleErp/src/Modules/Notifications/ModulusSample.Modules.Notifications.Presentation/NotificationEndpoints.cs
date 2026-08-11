using ModulusSample.Modules.Notifications.Presentation.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace ModulusSample.Modules.Notifications.Presentation;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationHub(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<NotificationHub>("/hubs/notifications");
        return endpoints;
    }
}
