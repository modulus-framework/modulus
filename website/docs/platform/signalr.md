---
sidebar_position: 7
---

# SignalR

Modulus provides real-time communication via SignalR.

## Setup

```csharp
services.AddModulusSignalR(config);
```

(`EnableDetailedErrors` is Development-only — never shipped to clients in
production.)

## Hub

Hubs derive from the generic `ModulusHub<TClient>` (namespace
`Modulus.SignalR`), which requires the ambient user + tenant:

```csharp
public sealed class NotificationHub(ICurrentUser user, ICurrentTenant tenant)
    : ModulusHub<INotificationClient>
{
    public async Task SendNotification(string message)
    {
        await Clients.All.ReceiveNotification(message);
    }
}
```

Module hubs are discovered via `IModuleHub` registrars
(`AddModuleHubs(assemblies)` / `MapModuleHubs(app)`).

## Usage

Publish integration events; registered `IRealtimeEventMapping<TEvent>`
mappings fan them out to clients:

```csharp
public sealed class OrderPlacedHandler(IRealtimeBus realtime)
    : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public async Task HandleAsync(OrderPlacedIntegrationEvent @event, CancellationToken ct)
    {
        await realtime.PublishAsync(@event, ct);
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

Tenant-scoped groups are supported:

```csharp
// Join a group
await Groups.AddToGroupAsync(Context.ConnectionId, "admin");

// Send to group
await Clients.Group("admin").SendAsync("update", data);
```

## Backplane

For multi-instance deployments (kept in the opt-in
`Modulus.SignalR.Backplane` package so the Redis/Azure SDKs stay out of
`Modulus.Platform`):

```bash
modulus app MyApp --signalr redis
```

```csharp
services.AddModulusSignalR(config).AddRedisBackplane(config);
// or .AddAzureBackplane(config);
```

## See Also

- [Platform Overview](overview) — Other platform services
