# Modulus.Messaging: configuration reference

Defaults are the values in the code. Settings are set on the bus (`ConfigureDefaults`), on one consumer
(`AddConsumer(..., o => ...)`) or, where stated, on a transport or outbox. The narrowest scope wins (RULE-14): a call,
then a message, then a consumer or saga, then an endpoint, then the bus.

## Bus (`MessagingBusBuilder`)

| Call | Default | Purpose |
|---|---|---|
| `AddConsumer<TConsumer, TMessage>(endpoint?, configure?)` | endpoint = consumer name in kebab-case | Registers a consumer for a message type on an endpoint. |
| `AddConsumer<TConsumer, TMessage>(ConsumerDefinition<TConsumer>)` | | Endpoint and settings from one class. |
| `AddBatchConsumer<TConsumer, TMessage>(...)` | | Batches by `BatchSize` and `BatchWindow`. |
| `AddRequestClient<TResponse>()` | | Registers the reply handler for `IRequestClient.GetResponse`. |
| `ConfigureDefaults(configure)` | nothing | Settings every consumer inherits unless it sets its own. |
| `UseTransport(factory)` | | A custom transport. |
| `UseInMemory()` / `UseRabbitMq(...)` / `UsePostgreSql(...)` | | The transports. |
| `UseEndpointNameFormatter(f)` | none | Maps logical endpoint names to physical ones (environment prefixes). |
| `UseMaxMessageSize(bytes)` | 1 MiB | A larger message is refused at send with `MessageTooLargeException`. |
| `UseDeployOnly()` | off | Declares the topology and exits; for a deploy job with admin rights (NFR-11). |
| `UseReplyEndpoint(name)` | generated | The endpoint replies to requests arrive on. |

## Consumer (`ConsumerOptions`)

| Setting | Default | Purpose |
|---|---|---|
| `Retry` (`RetryPolicy`) | none | `Immediate(n)`, `Interval(n, d)`, `Intervals(...)`, `Incremental(...)`, `Exponential(n, min, max)`; `.Ignore<TException>()` sends that exception straight to `_error`. |
| `Redelivery` (`RedeliveryPolicy`) | none | Delayed redeliveries after the retries are exhausted: `Intervals(d1, d2, ...)`. |
| `ConsumeTimeout` | none | Cancels a consume attempt that runs longer than this. |
| `RateLimit` | none | Messages per time window for the endpoint. |
| `KillSwitch` (`KillSwitchOptions`) | none | Pauses the endpoint when the failure ratio trips. See below. |
| `CircuitBreaker` (`CircuitBreakerOptions`) | none | Opens after consecutive failures and tests with one trial message. See below. |
| `TimeToLive` | none | Messages older than this go to `_skipped` unprocessed (RULE-08). |
| `Priority` | none | Higher is taken first where the transport supports it. Not on RabbitMQ. |
| `Ordered` | false | Strict send order per partition (`[PartitionKey]`, or the endpoint as one partition). |
| `ConcurrentMessageLimit` | 1 | Messages of the endpoint handled at once. Above 1 needs a concurrent transport (RabbitMQ, PostgreSQL, InMemory); otherwise startup fails. |
| `SingleActiveConsumer` | false | RabbitMQ only: one active consumer across hosts. |
| `RequireTenant` | false | A message with no company is parked with `no-tenant`. |
| `MaxDeliveryCount` | 20 | A delivery past this count is parked with `delivery-limit` without running the consumer (D3). Must be at least 1. |
| `EncryptPayload` | false | Seals the message body with AES-256-GCM (FR-22). |
| `BatchSize` / `BatchWindow` | | For batch consumers. |

### `KillSwitchOptions`

| Setting | Default |
|---|---|
| `FailureRatio` | 0.5 |
| `WindowSize` | 10 |
| `RestartTimeout` | 30 s |

### `CircuitBreakerOptions`

| Setting | Default |
|---|---|
| `FailureThreshold` | 5 consecutive failures |
| `OpenDuration` | 30 s |

## RabbitMQ (`RabbitMqTransportOptions`)

| Setting | Default | Purpose |
|---|---|---|
| `HostName` | `localhost` | |
| `Port` | 5672 | |
| `UserName` / `Password` | `guest` / `guest` | Take these from configuration or a secret store (NFR-09). |
| `VirtualHost` | `/` | |
| `ClientName` | `modulus-messaging` | Shown in the RabbitMQ management UI. |
| `UseTls` | false | Turn on outside development. |
| `TlsServerName` | none | Expected name on the broker certificate. |
| `TlsVersion` | TLS 1.2 and 1.3 | |
| `TlsCertificateValidation` | none (the system's validation) | A custom validation callback. |
| `MaxQueueLength` | none | Set before the queue is created; existing queues keep their arguments (D12). |
| `Prefetch` | 16 | Unacknowledged messages per consumer channel. Raised to the endpoint's `ConcurrentMessageLimit` when that is higher, so the limit can be reached. |
| `StopTimeout` | 30 s | Drain time on shutdown (NFR-18). |
| `ReconnectMinDelay` / `ReconnectMaxDelay` | 1 s / 60 s | Reconnect back-off (NFR-02). |

## PostgreSQL (`PostgreSqlTransportOptions`)

| Setting | Default | Purpose |
|---|---|---|
| `ConnectionString` | empty (required) | Use `SSL Mode=VerifyFull` outside development (NFR-09). |
| `Schema` / `Table` | `public` / `messaging_queue` | |
| `CreateSchema` | true | Creates the queue table on first use. |
| `TenantRowLevelSecurity` | false | Row-level isolation of the queue table (D7; opt-in). |
| `TransportRole` | `modulus_transport` | The database role the transport runs as, for RLS. |
| `BatchSize` | 16 | Rows claimed per poll. |
| `PollInterval` | 200 ms | |
| `LeaseDuration` | 5 min | How long a claim survives a dead host. |
| `StopTimeout` | 30 s | |
| `ParkedRetention` | none (kept) | Purges parked rows older than this. |
| `PurgeInterval` | 1 h | |
| `QueueDepthInterval` | 15 s | How often the queue-depth gauge is read. |

## Npgsql outbox and inbox (`NpgsqlOutboxOptions`)

| Setting | Default | Purpose |
|---|---|---|
| `Table` | `messaging_outbox` | |
| `PollInterval` | 1 s | |
| `BatchSize` | 100 | |
| `LeaseDuration` | 30 s | |
| `RunDispatcher` | true | Set false to run the dispatcher elsewhere. |
| `CreateSchema` | true | |

## Headers the bus sets

| Header | Meaning |
|---|---|
| `mod-tenant-id` | The company the message was sent for. |
| `mod-traceparent` | W3C trace context. |
| `mod-expires-at` | TTL deadline. |
| `mod-partition-key` | Partition of an ordered consumer. |
| `mod-sequence` | Position in the partition, kept across redeliveries. |
| `mod-delivery-count` / `mod-redelivery-count` | Deliveries so far, and redeliveries under the policy. |
| `mod-encrypted-type` | The message's URN, for an encrypted body. |
| `mod-request-id` / `mod-reply-to` | Request and reply correlation. |
| `mod-skip-reason` | Why a message went to `_skipped`. |
