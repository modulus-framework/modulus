# modulus add-messaging

Adds the message bus to an API app: the packages, the bus in the host, the settings and the connection string.

```bash
modulus add-messaging --transport postgresql
```

## Transports

| Value | What it uses | Use it for |
|---|---|---|
| `postgresql` (default) | A queue table in PostgreSQL, with the transactional outbox and inbox | Durable messages. Recommended. |
| `rabbitmq` | RabbitMQ with quorum queues and publisher confirms | When a broker is already part of the platform. |
| `inmemory` | No broker; messages are lost when the process exits | Development and tests only. |

## What it changes

- **The API project:** the Messaging packages for the transport.
- **The host:** `AddModulusMessaging` with the transport, and for PostgreSQL the outbox and inbox registrations.
- **Settings:** `Messaging:Transport`, and the connection string (`ConnectionStrings:Messaging` for PostgreSQL, `Messaging:RabbitMq` for RabbitMQ). The development settings name a local database; set the production connection string in the environment.

Running it again changes nothing that is already in place.

## After it runs

- Set `ConnectionStrings:Messaging` (PostgreSQL) or the RabbitMQ password in the environment for production.
- Register a consumer with `AddConsumer` in a module's composition root.
- The outbox and inbox tables are created on start. For a schema that migrations own, use the scripts from `MessagingNpgsqlOutboxExtensions.OutboxSchemaScript()` and `MessagingNpgsqlInboxExtensions.InboxSchemaScript()`.
