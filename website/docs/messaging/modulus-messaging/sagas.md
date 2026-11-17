---
sidebar_position: 4
---

# Sagas

A saga keeps the state of a long-running process: an order that waits for a payment, a request that waits for an answer, a process with a deadline. Modulus.Messaging offers two styles. Both persist the state through a saga repository, and both save the state before anything the saga asks for is sent.

## State machine sagas

Describe the states and the events, then the reactions:

```csharp
public sealed class OrderSaga : SagaInstance { public decimal Total { get; set; } }

public sealed class OrderMachine : SagaStateMachine<OrderSaga>
{
    public OrderMachine()
    {
        var awaitingPayment = State("AwaitingPayment");
        var submitted = Event<OrderSubmitted>().CorrelateBy(m => m.OrderId);
        var paid = Event<PaymentReceived>().CorrelateBy(m => m.OrderId);

        Initially(
            When(submitted)
                .Then(context => context.Saga.Total = context.Message.Total)
                .TransitionTo(awaitingPayment));

        During(awaitingPayment,
            When(paid)
                .Then(context => context.Publish(new OrderPaid(context.Message.OrderId)))
                .Complete());
    }
}
```

Register it on an endpoint with `b.AddSaga<OrderMachine, OrderSaga>("orders")` and a repository. The reaction helpers are `Then`, `TransitionTo`, `Complete`, `Ignore`, `If`, `IfElse` and `Catch`. `Catch` discards the attempt's state and sends nothing, then runs the compensation.

## Consumer sagas

A consumer saga implements its handlers on the saga class itself:

```csharp
public sealed class PaymentSaga : SagaInstance,
    ICorrelatedBy<PaymentStarted>,
    IInitiatedBy<PaymentSaga, PaymentStarted>
{
    public static Guid CorrelationKeyOf(PaymentStarted message) => message.PaymentId;

    public void Initiate(SagaContext<PaymentSaga, PaymentStarted> context) => CurrentState = "Started";
}
```

Register it with `b.AddConsumerSaga<PaymentSaga>("payments")`. A message type that the saga handles needs an `ICorrelatedBy` implementation.

## Requests

A saga can ask another endpoint a question. The answer, or the consumer's failure, comes back to the saga as a message it declares:

```csharp
context.Request(new Uri("queue:pricing"), new QuoteRequested(orderId));
```

The answer arrives as a `RequestReply<TResponse>` for the request id, and a failure as a `RequestFault`. Declare them as events correlated by the request id. A saga has one outstanding request per instance.

## Request timeouts

A request can give up after a time:

```csharp
context.Request(new Uri("queue:pricing"), new QuoteRequested(orderId), TimeSpan.FromSeconds(30));
```

When the time passes without an answer, the saga receives a `SagaRequestTimedOut` message on its own endpoint. Declare it like any other event and decide what the timeout means: retry, compensate or complete. The rules are:

- An answer, or a fault, for the open request closes it. Its timeout is then dropped, not parked.
- A later request replaces the open one, and the earlier timeout is dropped.
- Completing the saga cancels every timeout it scheduled.
- The timeout is stored with the saga, so it survives a restart.

## Schedules

`context.Schedule(delay, message)` publishes a message later. It is stored with the saga, so completing the saga cancels it.

## Saga repositories

| Repository | Registration | Notes |
|---|---|---|
| In memory | `AddInMemorySagaRepository<TSaga>()` | Tests and development. |
| Entity Framework Core | `AddEntityFrameworkSagaRepository<TSaga, TContext>()` | Shares the application's database context. |
| PostgreSQL (JSONB) | `AddPostgreSqlSagaRepository<TSaga>()` | Registered per scope, so it joins the scope's unit of work. Call `AddModulusMessagingNpgsqlUnitOfWork()` (or the Npgsql outbox) as well. |

Every repository checks the version on each save and delete, so two workers cannot both update the same saga, and no update is lost.

## Rules

- The saga state, the inbox record and the outgoing messages commit together, or roll back together (with the PostgreSQL outbox and inbox).
- A message for a saga that does not exist yet waits in the queue, and is retried, until the saga starts.
