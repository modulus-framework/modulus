---
sidebar_position: 7
---

# SignalR

Modulus provides real-time communication via SignalR.

## Setup

```csharp
services.AddModulusSignalR(config);
```

## Hub

```csharp
public sealed class NotificationHub : ModulusHub
{
    public async Task SendNotification(string message)
    {
        await Clients.All.SendAsync("notification", message);
    }
}
```

## Usage

### Server-Side

```csharp
public sealed class OrderPlacedHandler(IRealtimeBus realtime)
    : IIntegrationEventHandler<OrderPlacedEvent>
{
    public async Task HandleAsync(OrderPlacedEvent @event)
    {
        await realtime.SendToGroupAsync(
            $"tenant:{@event.TenantId}",
            "orderPlaced",
            new { OrderId = @event.OrderId });
    }
}
```

### Client-Side

```javascript
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/notifications")
    .build();

connection.on("notification", (message) => {
    console.log("Notification:", message);
});

await connection.start();
```

## Groups

```csharp
// Join a group
await Groups.AddToGroupAsync(Context.ConnectionId, "admin");

// Send to group
await Clients.Group("admin").SendAsync("update", data);
```

## Backplane

For multi-instance deployments:

```bash
modulus app MyApp --signalr-backplane redis
```

```json
{
  "SignalR": {
    "Backplane": "redis",
    "RedisConnectionString": "localhost:6379"
  }
}
```

## See Also

- [Platform Overview](overview) — Other platform services
