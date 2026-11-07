---
sidebar_position: 8
title: Webhooks
description: Deliver integration events to external HTTP endpoints, signed according to Standard Webhooks.
---

# Webhooks

`Modulus.Webhooks` delivers integration events to endpoints outside your system. Each delivery is signed according to [Standard Webhooks](https://www.standardwebhooks.com/).

## Registration

```csharp
builder.Services.AddModulusWebhooks(builder.Configuration, w => w
    .AddEvent<ProductCreatedIntegrationEvent>("A product was created"));

builder.Services.AddModulusWebhooksStore<WebhooksDbContext>();
app.MapModulusWebhooks("/api/webhooks");
```

`AddEvent` also registers a handler, so events arrive from the module bus, the outbox relay or a broker consumer. Each matching subscription of the event's tenant gets one delivery row. A unique index on (subscription, event) makes redelivery harmless.

Subscriptions filter by exact event name or by prefix (`catalog.*`).

## Signing

Each delivery carries three headers:

| Header | Value |
|--------|-------|
| `webhook-id` | `msg_{deliveryId}`, the same on every retry |
| `webhook-timestamp` | Unix time of the attempt |
| `webhook-signature` | `v1,<base64 HMAC-SHA256>` |

Secrets look like `whsec_…` and are encrypted with ASP.NET Data Protection. Persist and share the Data Protection key ring in production, or stored secrets become unreadable. `rotate-secret` keeps the old secret signing for a configured overlap period.

Receivers verify the signature with `StandardWebhooks.Verify`.

## Delivery

- Deliveries are sent by a hosted service. Leases make it safe with several replicas.
- Retries follow the Standard Webhooks schedule. A longer `Retry-After` from the receiver wins, up to a day.
- An endpoint that answers `410`, or keeps failing for `DisableAfterFailingFor`, is disabled.

## Security

URLs are checked when they are saved: HTTPS only, no credentials or fragments, and no loopback, private or reserved addresses. At send time the HTTP handler resolves DNS again and connects only to public addresses, with redirects turned off. `AllowHttp` and `AllowPrivateNetworks` exist for Development only.

## Management API

Requires the `webhooks:manage` permission and is scoped to the tenant. It covers event types, subscription CRUD (the secret is returned only on create and rotate), `test`, deliveries by status, delivery detail and `retry`.

## Command-line support

```bash
modulus add-webhooks --events ProductCreated
```

This creates a store module with its own database context, wires the host, and adds a test class. Run it again to add more events.
