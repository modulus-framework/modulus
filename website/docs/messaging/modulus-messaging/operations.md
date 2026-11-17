---
sidebar_position: 7
---

# Operations runbook


This page is for the people who run a service that uses the bus.

## What the signals mean

| Signal | Meaning | First look |
|---|---|---|
| `messaging.parked.messages` (counter, label `messaging.destination.name`) | A message moved to an `_error` queue (a consumer failed past its retries) or to `_skipped` (no consumer, expired, or no tenant). | The `_error` queue of that endpoint and its fault message. |
| `messaging.process.duration` (histogram, seconds) | Time a consume attempt takes, from receive to commit. | Slow consumers, a database or broker under load. |
| `messaging.client.consumed.messages` / `messaging.client.sent.messages` | Throughput. | A drop with no deploy means the bus or the broker stopped delivering. |
| `messaging.inbox.duplicates` | A redelivery was skipped because the message was already applied. | A small steady rate is normal after a restart. A jump means redeliveries are piling up. |
| `modulus.messaging.queue.depth` (gauge, PostgreSQL transport, label `queue`) | Rows waiting in each queue, claimed or parked, at the last sample. | A queue that never empties. |

## Parked messages

1. **Look before you replay.** Open the `_error` queue for the endpoint and read the fault: the exception type, the message, and the attempt count. The `messaging.parked.messages` counter and the log line `Parked message ... in _error ... reason ...` name the same reason.
2. **Fix the cause first.** A replay sends the messages back to the endpoint with their original message ids. A message that fails the same way is parked again.
3. **Replay in batches.** The operator endpoint replays up to `limit` messages (default 100, at most 1000):

   ```
   POST /messaging/errors/{endpoint}/replay?limit=100
   ```

   The response is `{ "endpoint": "...", "moved": N }`. It needs the `messaging:errors:replay` permission, which is `Critical`: grant it to operators only. Every replay is logged with the user who ran it.
4. **Replayed messages are applied once.** The consumer's inbox records each message id, so a replay of a message that was in fact applied is skipped, not applied twice. A consumer without an inbox must be idempotent itself.
5. **A poison message** (one that keeps failing) is parked again after each replay. Do not replay it in a loop. Move it aside, or fix the consumer and deploy.
6. **Delivery-limit parking** (`reason: delivery-limit`) means a message crashed its host on every attempt. Investigate the host's crash logs before any replay.

The CLI replay is not built (decided; see the plan). The operator endpoint is the route.

## Outages

- **RabbitMQ.** The transport reconnects with a back-off from 1 s to 60 s. While the broker is down, sends fail and the health check reports unhealthy. Messages in flight are requeued by the broker when the connection drops, so a handler may run again: consumers must be idempotent (the inbox covers the consumer side).
- **PostgreSQL.** Sends fail while the database is down, and polling resumes when it returns. Scheduled messages are kept in the table, so they survive the outage.
- **A stop** drains in-flight messages for up to 30 s (`stop timeout`). Messages still unfinished go back to the broker or the queue.

## Scheduled messages

A scheduled message is cancelled by its token, through `IMessageScheduler.CancelScheduled`. This works on PostgreSQL and through the outbox. It does not work on RabbitMQ (decided; the TTL-bucket scheduling cannot search by token). A cancel that returns false means the message was already delivered, or the token was never stored.

## Rolling upgrade across the URN change

Receivers accept both the URN and the .NET name, so the order is:

1. Deploy every consumer on a build that reads both forms (this build).
2. Turn on `UseLegacyWireTypeNames()` on the senders that still have consumers on an older build.
3. When no older consumer is left, remove `UseLegacyWireTypeNames()` and deploy the senders again.

The switch is per bus. It has to be set at startup, before the first message is sent. Nothing has been released yet, so no fleet is mixed today.

## Schema rollout

- The PostgreSQL queue table is versioned in the queue schema script in the repository (docs/messaging/postgresql-queue-schema.sql) (the header names the version). `SchemaScriptTests` checks that the script matches what the transport runs, and that it only adds.
- Apply the script before the new code starts. Old instances keep working while it runs, because it adds tables, columns and indexes and never drops, renames or retypes one.
- A change that must remove or retype a column is an expand/contract change: add the new column, move the data, switch the code, and only then remove the old column in a later release that is planned with the owner.
- The saga and outbox tables follow the same rule. Their schema scripts are `PostgreSqlSagaRepository<TSaga>.SchemaScript`, `MessagingNpgsqlOutboxExtensions.OutboxSchemaScript` and `MessagingNpgsqlInboxExtensions.InboxSchemaScript`.

## Health

Expose the health checks of the host (`/health/ready` in the generated hosts). The messaging health checks are: RabbitMQ healthy when a passive declare on `amq.direct` answers within 5 s; PostgreSQL healthy when `SELECT 1` succeeds; the in-memory transport always reports healthy, because it has no broker.
