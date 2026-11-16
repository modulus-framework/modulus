# Migrating from MassTransit v8 to Modulus.Messaging

This guide maps the MassTransit v8 APIs to Modulus.Messaging (NFR-19). It covers the common shapes: publish and send,
consumers, retries and redelivery, sagas, the transactional outbox and inbox, and the transports. Where a MassTransit
feature has no equivalent, the guide says so.

## Registration

| MassTransit v8 | Modulus.Messaging |
|---|---|
| `services.AddMassTransit(x => { ... })` | `services.AddModulusMessaging(b => { ... })` |
| `x.AddConsumer<TConsumer>()` | `b.AddConsumer<TConsumer, TMessage>("endpoint")`: the endpoint is named explicitly, one consumer per message type per endpoint |
| `x.UsingRabbitMq((ctx, cfg) => ...)` | `b.UseRabbitMq(o => { o.HostName = ...; o.Port = ...; o.UserName = ...; o.Password = ...; })` |
| `x.UsingInMemory()` | `b.UseInMemory()` |
| (EF Core transactional outbox) `AddEntityFrameworkOutbox<TDbContext>` | `services.AddModulusMessagingEntityFrameworkOutbox<TDbContext>()` |
| (EF Core inbox) `AddEntityFrameworkOutbox`, deduplication | `services.AddModulusMessagingEntityFrameworkInbox<TDbContext>()` |
| Health checks (`AddMassTransitHealthChecks`) | `services.AddHealthChecks().AddModulusMessagingHealthCheck()` |

Modulus.Messaging has no Kafka or Azure Service Bus transport. The transports are in-memory, RabbitMQ and PostgreSQL.

## Publish, send and consume

| MassTransit v8 | Modulus.Messaging |
|---|---|
| `IPublishEndpoint.Publish(msg)` | `IBus.Publish(msg)` or `IPublishEndpoint` (publish reaches every consumer of the type) |
| `ISendEndpointProvider.GetSendEndpoint(uri).Send(msg)` | `IBus.Send(new Uri("queue:endpoint"), msg)` |
| `IConsumer<T>.Consume(ConsumeContext<T> ctx)` | unchanged: `IConsumer<T>` with `ConsumeContext<T>` |
| `ctx.Message`, `ctx.CancellationToken`, `ctx.MessageId` | unchanged; `ctx.Endpoint` names the endpoint; `ctx.TenantId` names the company |
| Headers (`ctx.Headers`) | `ctx.Envelope.Headers` (keys are `mod-*`) |
| `ctx.RespondAsync` / `IRequestClient<T>` (`GetResponse`) | `ConsumeContext.RespondAsync(response)` and `IRequestClient.GetResponse<TRequest, TResponse>(address, request, timeout)`. Register the response type with `AddRequestClient<TResponse>()`. Timeouts raise `RequestTimeoutException` |
| Message scope per consume (DI) | unchanged: a new DI scope per attempt (RULE-07) |

## Retries, redelivery and faults

| MassTransit v8 | Modulus.Messaging |
|---|---|
| `r.Immediate(3)` | `o.Retry = RetryPolicy.Immediate(3)` |
| `r.Interval(3, TimeSpan.FromSeconds(1))` | `o.Retry = RetryPolicy.Interval(3, TimeSpan.FromSeconds(1))` |
| `r.Exponential(...)` | `RetryPolicy.Exponential(...)` |
| `r.Ignore<TException>()` | `RetryPolicy.Immediate(3).Ignore<TException>()`: that exception sends the message straight to `_error` |
| `r.Handle<TException>()` (retry only some) | not supported directly; `Ignore` is the inverse. Throw `MessageParkException` to park at once |
| `r.Redelivery` (delayed redelivery) | `o.Redelivery = RedeliveryPolicy.Intervals(...)` |
| `_error` queue and `Fault<T>` | `_error` queue; `Fault<TMessage>` is published when a message is parked. A fault of an encrypted message is encrypted too |
| `_skipped` | `_skipped` for messages with no consumer, expired, or without a required company |
| Per-endpoint kill switch | `o.KillSwitch`: pauses the endpoint after too many failures, then restarts with an empty window |
| Circuit breaker | `o.CircuitBreaker = new CircuitBreakerOptions { FailureThreshold, OpenDuration }`: opens after consecutive failures and tests the consumer with one trial message before it resumes |
| Rate limit | `o.RateLimit` |
| Concurrency limit | `o.ConcurrentMessageLimit` (needs a transport that delivers concurrently: RabbitMQ and PostgreSQL) |
| `UseMessageRetry` per-consumer | `b.AddConsumer<T, M>("endpoint", o => { ... })`: options set per consumer, or with `b.ConfigureDefaults(...)` for all |

Delivery count: a message delivered more than `MaxDeliveryCount` times (default 20) is parked as `delivery-limit`, so a
message that crashes its host on every attempt stops looping. MassTransit has no direct equivalent.

## Sagas

| MassTransit v8 | Modulus.Messaging |
|---|---|
| `MassTransitStateMachine<TInstance>` | `SagaStateMachine<TSaga>` |
| `State(...)`, `Event(() => ...)` | `State("name")`, `Event<TMessage>().CorrelateBy(m => m.Id)` |
| `Initially(When(E).Then(...).TransitionTo(S))` | `Initially(When(E).Then(...).TransitionTo(S))` |
| `During(S, When(E).Finalize())` | `During(S, When(E).Complete())` |
| `.Schedule(...)` / timeouts | `context.Schedule(delay, message)`; a completed saga cancels its pending timeouts (RabbitMQ cannot cancel: see below) |
| `.Ignore(E)` | `When(E).Ignore()` |
| `If` / `IfElse` activity | `When(E).If(condition, block)` / `.IfElse(condition, whenTrue, whenFalse)` |
| `Catch<T>` activity | `When(E).Then(...).Catch<T>(compensation => ...)`: the attempt is discarded and the compensation runs on the saga as it was |
| `Request` / `Respond` (saga activities) | `context.Request(address, message)` in a saga reaction. The answer is a message the saga declares with `Event`; a consumer failure is the `RequestFault` event, correlated by `RequestId`. One request is outstanding per saga instance. A request timeout is not built: use `context.Schedule` |
| `InstanceState` repository (EF Core, Redis, MongoDB) | in-memory (`AddInMemorySagaRepository<TSaga>`), EF Core (`AddEntityFrameworkSagaRepository<TSaga, TContext>`) or PostgreSQL JSONB (`AddPostgreSqlSagaRepository<TSaga>`). Redis and MongoDB have no repository |
| Consumer sagas (`ISaga` + `InitiatedBy`) | `AddConsumerSaga<TSaga>(endpoint)` with `IInitiatedBy<TSaga, TMessage>` and `ICorrelatedBy<TMessage>` |

Differences that matter for a port:
- A saga's `Complete()` (not `Finalize()`) removes its instance.
- Saga instances are scoped by company when a company is set: the same correlation id in two companies is two sagas.
- The state-machine state is a `SagaInstance` POCO serialised as JSON. Plan a migration of existing MassTransit
  instances if they must carry over: they are not compatible.

## Transactional outbox and inbox

- MassTransit's EF Core outbox maps to `AddModulusMessagingEntityFrameworkOutbox<TDbContext>()`. Publishes and sends
  made inside a scope with an open transaction are written with it and sent after the commit (RULE-01).
- The inbox records each handled message per consumer, in the consumer's transaction (RULE-04, RULE-06). A redelivered
  message is skipped and counted (`messaging.inbox.duplicates`). Purge the records with `InboxMaintenance.PurgeExpiredAsync`.
- Both need a migration in the consuming service: the outbox table, and the inbox table with `TenantId` (added in this
  version). Run `dotnet ef migrations add` for the consuming DbContext.
- On PostgreSQL without EF Core, use the Npgsql pair: `AddModulusMessagingNpgsqlOutbox()` and `AddModulusMessagingNpgsqlInbox()`.
  A publish needs an open transaction: `await uow.BeginAsync(ct)` on `INpgsqlUnitOfWork`, then `uow.CommitAsync(ct)`.
  The schema is `OutboxSchemaScript()` and `InboxSchemaScript()`.

## Transports

| MassTransit v8 | Modulus.Messaging |
|---|---|
| RabbitMQ with quorum queues | `UseRabbitMq`: quorum queues per endpoint, `_error` and `_skipped` queues, publisher confirms |
| RabbitMQ priority | not on quorum queues (decision 2026-11-15): a quorum queue refuses `x-max-priority`. Use an endpoint per priority class |
| RabbitMQ delayed-message plugin | not used: scheduling uses TTL buckets. A scheduled message cannot be cancelled on RabbitMQ; cancel it through the outbox or PostgreSQL |
| Scheduled message cancel | RabbitMQ: unsupported; PostgreSQL and in-memory: `CancelScheduled(token)` |
| Kafka, Azure Service Bus, Amazon SQS | not supported. Use RabbitMQ or PostgreSQL |
| PostgreSQL | `UsePostgreSql`: one queue table (`docs/messaging/postgresql-queue-schema.sql`), `FOR UPDATE SKIP LOCKED` claims |

## Observability

| MassTransit v8 | Modulus.Messaging |
|---|---|
| `ActivitySource` (`MassTransit`) | `ActivitySource` `Modulus.Messaging`: producer span per publish and send, consumer span per attempt; `traceparent` travels in `mod-traceparent` |
| Metrics | meter `Modulus.Messaging`: `messaging.client.sent.messages`, `messaging.client.consumed.messages`, `messaging.process.duration`, `messaging.parked.messages`, `messaging.inbox.duplicates`, `modulus.messaging.queue.depth` (PostgreSQL) |
| `messaging.system` | the transport name: `rabbitmq`, `postgresql`, `in-memory` |

## Security

- Payload encryption is opt-in: mark a message type `[EncryptedMessage]` or set `EncryptPayload` on the consumer. The
  body is sealed with AES-256-GCM and the key comes from `IMessageEncryptionKeys` (`StaticMessageEncryptionKeys` takes
  base64 32-byte keys). MassTransit has no built-in equivalent.
- Keys are secrets. They must come from a secret store or user secrets, never from source control.
- Companies: a message carries its company (`mod-tenant-id`) and the consumer runs in it. Register
  `AddModulusTenantMessaging()` after the multi-tenancy setup.

## Checklist for a port

1. Replace `AddMassTransit` with `AddModulusMessaging`; name every endpoint explicitly.
2. Map each consumer to `AddConsumer<TConsumer, TMessage>("endpoint", options)`.
3. Port retry and redelivery policies; check `Ignore` and `Handle` exceptions.
4. Port sagas. Replace `Finalize` with `Complete`. Re-check timeouts on RabbitMQ.
5. Replace the EF outbox and inbox registrations; add the migrations.
6. Port transports; drop any unsupported ones (Kafka, Service Bus, SQS).
7. Run the port's integration tests on the new bus, and read `_error` and `_skipped` after one full load test before
   production.
