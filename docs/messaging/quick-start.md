# Modulus.Messaging: quick start

Five minutes to a working consumer, per transport. The full option list is in
[configuration-reference.md](configuration-reference.md); the MassTransit port is in
[migration-from-masstransit.md](migration-from-masstransit.md).

## 1. Declare a message and a consumer

```csharp
using Modulus.Messaging;

[MessageUrn("urn:orders:order-submitted:v1")]   // the wire identity; keep it stable once released (RULE-10)
public sealed record OrderSubmitted(Guid OrderId);

public sealed class BillingConsumer : IConsumer<OrderSubmitted>
{
    public Task Consume(ConsumeContext<OrderSubmitted> context)
    {
        // Your work. Throw to retry; call context.Redeliver(TimeSpan.FromMinutes(1)) to come back later.
        return Task.CompletedTask;
    }
}
```

`[MessageUrn]` is optional, but a message without it is identified by its .NET name, so renaming the class
changes its identity. Put the attribute on every message you release.

## 2. Pick a transport

Every transport uses the same consumer and publish code. Only the registration changes (BR-09).

### In-memory (tests and development)

```csharp
services.AddModulusMessaging(b =>
{
    b.AddConsumer<BillingConsumer, OrderSubmitted>("billing");
    b.UseInMemory();
});
```

Nothing is durable: messages are lost if the process exits.

### RabbitMQ (quorum queues, publisher confirms)

```csharp
services.AddModulusMessaging(b =>
{
    b.AddConsumer<BillingConsumer, OrderSubmitted>("billing");
    b.UseRabbitMq(o =>
    {
        o.HostName = configuration["RabbitMq:Host"]!;
        o.UserName = configuration["RabbitMq:User"]!;
        o.Password = configuration["RabbitMq:Password"]!;   // from a secret store, never from source
        o.UseTls = true;                                    // turn on outside development (NFR-09)
    });
});
```

Requires RabbitMQ 3.13 or later. Priority does not apply on RabbitMQ (quorum queues); scheduled messages
cannot be cancelled there.

### PostgreSQL (one queue table, `FOR UPDATE SKIP LOCKED`)

```csharp
services.AddModulusMessaging(b =>
{
    b.AddConsumer<BillingConsumer, OrderSubmitted>("billing");
    b.UsePostgreSql(o => o.ConnectionString = configuration.GetConnectionString("Messaging")!);
});
```

The queue table is created on first use (`CreateSchema`). For a database with managed schemas, run
`docs/messaging/postgresql-queue-schema.sql` yourself and set `CreateSchema = false`.

## 3. Publish and send

```csharp
// Publish: every consumer of the type (or of its interfaces and base types) gets one copy.
await bus.Publish(new OrderSubmitted(orderId), ct);

// Send: one named endpoint; exactly one instance handles it.
await bus.Send(new Uri("queue:billing"), new OrderSubmitted(orderId), ct);
```

## 4. Make publishing transactional (optional, RULE-01)

A message published inside a transaction is sent only if the transaction commits.

- **EF Core:** `services.AddModulusMessagingEntityFrameworkOutbox<TDbContext>();` then add the outbox table to your
  migrations.
- **Npgsql:** `services.AddModulusMessagingNpgsqlOutbox();` and `AddModulusMessagingNpgsqlInbox();`. Begin
  the transaction with `INpgsqlUnitOfWork.BeginAsync` before publishing, then `CommitAsync`.
- **In memory:** `services.AddModulusMessagingInMemoryOutbox();` then call `IInMemoryMessageOutbox.CommitAsync`
  after your work. A scope that ends without a commit sends nothing. Not durable.

## 5. Retry and redelivery

```csharp
b.AddConsumer<BillingConsumer, OrderSubmitted>("billing", o =>
{
    o.Retry = RetryPolicy.Immediate(3).Ignore<FormatException>(); // a FormatException goes straight to _error
    o.Redelivery = RedeliveryPolicy.Intervals(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
});
```

A message that still fails goes to `billing_error`, with the exception in its fault headers. Replay it through
`POST /messaging/errors/billing/replay` (the Management package) after you fix the cause.

## 6. Test without a broker

```csharp
await using var harness = await MessagingTestHarness.StartAsync(
    b => b.AddConsumer<BillingConsumer, OrderSubmitted>("billing").UseInMemory());

await harness.Bus.Publish(new OrderSubmitted(Guid.NewGuid()));
```

`VirtualClock`, `FaultInjector` and `SagaHarness<TSaga>` are in `Modulus.Messaging.Testing`.

## Where to go next

- Every setting and its default: [configuration-reference.md](configuration-reference.md).
- Sagas (state machines, request steps, consumer sagas): the saga section of the
  [implementation plan](../Modulus.Messaging%20—%20Implementation%20Plan.md) lists what is built and how.
- Operating it (health checks, OpenTelemetry names, the parked-message replay endpoint): the observability and
  management sections of the same plan.
