---
sidebar_position: 4
title: Realtime
description: Push integration events to connected clients over SSE, with SignalR as an opt-in transport.
---

# Realtime

`Modulus.Realtime` pushes integration events, and any app message, to connected clients. Server-Sent Events (SSE) is the default transport: it is one-way, works through the BFF proxy and cookie sessions, and `EventSource` reconnects and resumes on its own. SignalR is opt-in (`Realtime:SignalR:Enabled`) for clients that need two-way calls, such as subscribing to topics while connected.

## Registration

```csharp
builder.Services.AddModulusRealtime(builder.Configuration, r => r
    .AddEvent<ProductCreatedIntegrationEvent>(
        e => RealtimeAudience.Permission("catalog:products:read"))
    .AddTopic("orders.*", permission: "orders:read"));

app.MapModulusRealtime();    // requires a signed-in user unless RequireAuthenticatedUser: false
```

Events are pushed under their `[IntegrationEventName]`. Names starting with `modulus.` are reserved.

## Publishing

Inject `IRealtimePublisher` and pick an audience:

```csharp
await publisher.PublishAsync("catalog.product-updated", payload,
    RealtimeAudience.Tenant);

await publisher.PublishAsync("orders.status", payload,
    RealtimeAudience.User(userId));
```

Audiences are always limited to the publishing tenant. `RealtimeAudience.Tenant`, `.Permission(p)`, `.User(id)`, `.ForUsers(ids)` and `.ForTopic(t)` are available, and `.RequirePermission(p)` narrows any of them.

## SSE endpoint

`GET {Path}/events?types=a,b.*&topics=x&lastEventId=` streams events:

- `modulus.ready` is sent first, with the connection id and retry interval.
- Heartbeats arrive as `modulus.heartbeat` events with empty data.
- The stream closes when the caller's token expires.
- An unknown topic returns `400`, a denied one `403`, and too many connections per user `429`.

A replay buffer (`ReplayBufferSize`, `ReplayWindow`) lets a reconnecting client resume with `Last-Event-ID`. If the id has been evicted, the client receives `modulus.reset`.

## Scaling out

`Modulus.Realtime.Redis` adds `AddRedisRealtimeBackplane(configuration)`, so every replica delivers events to its own connections. If Redis is down, delivery stays local to each node.

## Command-line support

```bash
modulus add-realtime --events ProductCreated --bff web,mobile
modulus add-realtime --signalr
```

This adds the registration, the `/realtime` route in each chosen BFF, and a test class.
