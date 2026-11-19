# Modulus.Messaging: implementation plan

Status date: **2026-11-16**. Source requirements: [BRS](Cobytelabs.Messaging%20—%20Business%20Requirements%20Specification%20(BRS).md) (v1.0 draft). The bus is named **Modulus.Messaging** (working name; the BRS open issue on the final name stays open).

This document is the single source of status. Where an older note disagrees with it, this document is right and the note is out of date.

## Summary

- **Built and tested:** the core bus, the InMemory, PostgreSQL and RabbitMQ transports, the EF Core and Npgsql outbox and inbox, state-machine and consumer sagas (including request steps, `If`/`IfElse`/`Catch`/`Ignore`), scheduling, TTL, priority (not on RabbitMQ), ordering, tenant isolation, tracing, metrics, logs, health checks, the Modulus host adapter, payload encryption, `[Sensitive]` masking, the operator replay endpoint, the `modulus add-messaging` CLI scaffolding, and a reconnecting RabbitMQ transport.
- **Gates:** G0 (conformance on InMemory) is met, but the consumer-shape generator is not built (it needs `samples/`). G1 (1M messages under random kills, zero lost, zero double-applied) passed on PostgreSQL on 2026-11-15 and again on 2026-11-16 on one development machine. G2 (25k msg/s with confirms) is **not met** on the development machine and is not judged yet. G3 (TradeFlow sample) is not started. G4 (production) is not reached.
- **Not built, by decision:** RabbitMQ priority and RabbitMQ cancel of scheduled messages (broker limits), the CLI form of replay, native AOT, and a circuit breaker separate from the kill switch.
- **Open:** see [Open tasks](#open-tasks). Most of them need the owner, the target hardware, the `samples/` folder, or a dedicated runner.

## Projects

| Project | Purpose |
|---|---|
| `Modulus.Messaging` | Core: bus, consumers, pipeline, retry, faults, requests, filters, observers, topology, tenant and encryption hooks |
| `Modulus.Messaging.Transports.InMemory` | Reference transport (no durability) |
| `Modulus.Messaging.Transports.RabbitMQ` | AMQP transport: quorum queues, confirms, TLS, reconnect back-off, TTL-bucket scheduling |
| `Modulus.Messaging.Transports.PostgreSql` | Queue-table transport: `FOR UPDATE SKIP LOCKED` claims, leases, scheduling, RLS (opt-in) |
| `Modulus.Messaging.Outbox.EntityFrameworkCore`, `.Outbox.Npgsql` | Transactional outbox |
| `Modulus.Messaging.Inbox.EntityFrameworkCore`, `.Inbox.Npgsql` | Idempotent inbox with tenant column |
| `Modulus.Messaging.Sagas` | State-machine and consumer sagas, the in-memory repository |
| `Modulus.Messaging.Sagas.EntityFrameworkCore`, `.Sagas.PostgreSql` | Saga repositories with optimistic concurrency |
| `Modulus.Messaging.Testing` | Test harness, virtual clock, fault injection, saga harness, transport conformance kit |
| `Modulus.Messaging.Modulus` | Host adapter: tenant bridge (`AddModulusTenantMessaging`) |
| `Modulus.Messaging.Management` | Operator API: replay parked messages (`POST /messaging/errors/{endpoint}/replay`) |

The CLI scaffolding (`modulus add-messaging`) is in `cli/`.

## Phases and gates (BRS section 13)

- **M0 Foundations, gate G0:** core API, InMemory transport, harness, conformance suite. **Met** (conformance runs on InMemory, PostgreSQL and RabbitMQ). The consumer-shape generator is **open** (needs `samples/`).
- **M1 Reliability, gate G1:** retries, redelivery, `_error`/`_skipped`, faults, outbox, inbox, flow control, circuit breaker. **G1 met** (chaos run passed, see below).
- **M2 RabbitMQ, gate G2:** transport, confirms, quorum queues, TLS, reconnect back-off. **G2 open:** 25k msg/s is not reached on the development machine (23.4k and 23.3k msg/s on 2026-11-16; 31–32k msg/s in an earlier run through the same Podman port proxy). Judged on target hardware only.
- **M3 PostgreSQL, gate G3:** transport, scheduler, TTL, ordering, sagas, saga repositories, tenant isolation. Built. **G3 open:** TradeFlow sample not started (needs `samples/`).
- **M4 Release, gate G4:** encryption, redaction, telemetry, health, host adapter, CLI, migration guide, API baseline, signed packages. Most built. **G4 open:** a product in production for two weeks; signed packages (needs the owner's certificate).

## Current verification (2026-11-16)

Run on this machine (Ryzen 5 8600G, 12 threads, Podman for containers). Unit suites on net10.0 unless noted.

| Suite | Result |
|---|---|
| `tests/unit/Modulus.Messaging.Tests` (core) | 141 of 141, net10.0 (2026-11-16, after the per-bus wire-name change) |
| `tests/unit/Modulus.Messaging.Sagas.Tests` | 29 of 29, both TFMs (2026-11-16) |
| `tests/unit/Modulus.Messaging.Modulus.Tests` (host adapter) | 7 of 7, both TFMs |
| `tests/unit/Modulus.Messaging.Outbox.EntityFrameworkCore.Tests` | 11 of 11, both TFMs (2026-11-16) |
| `tests/unit/Modulus.Messaging.Inbox.EntityFrameworkCore.Tests` | 4 of 4, both TFMs |
| `tests/unit/Modulus.Messaging.Sagas.EntityFrameworkCore.Tests` | 3 of 3, both TFMs (2026-11-16) |
| `tests/unit/Modulus.Messaging.Management.Tests` | 8 of 8, both TFMs |
| `tests/unit/Modulus.Analytics.Messaging.Tests` | 6 of 6, both TFMs |
| `tests/unit/Modulus.Identity.Messaging.Tests` | 16 of 16, both TFMs |
| `tests/integration/Modulus.Messaging.Transports.RabbitMQ.Integration.Tests` (Category=Integration, net10.0, Podman, 2026-11-16) | 28 of 28, including the broker-restart tests |
| `tests/integration/Modulus.Messaging.Transports.PostgreSql.Integration.Tests` (Category=Integration, net10.0, Podman, 2026-11-16) | 35 of 35, including the restart tests |
| Chaos G1 (`Category=Chaos`, PostgreSQL, 1,000,000 messages) | Pass, 2026-11-16: every message applied once, 102 worker kills, 16.5 min |
| Benchmark `MessagingBenchmarks.SendAndHandle` (in-memory, Release, 2026-11-16, after the per-bus wire-name change) | Median 4.51 µs, mean 4.52 µs, 2.83 KB allocated per message, with the default 1 MiB size limit on. 5 µs target met on this machine |
| Benchmark `MessagingThroughputBenchmarks` (in-memory, Release, 10,000 messages per batch, NFR-03) | Concurrency 1: 2.34 µs per message (about 428k msg/s); concurrency 8: 1.77 µs (about 564k msg/s). The 1M msg/s target is **not met** on this machine (12 threads; the target assumes 8 cores). Allocated 2.4 KB and 2.66 KB per message |
| Benchmark `MessagingStartupBenchmarks` (NFR-07) | Build the bus, register 100 consumers and start and stop the host: mean 314 µs, 500 KB allocated. Target under 50 ms: met. One consumer type on 100 endpoints; excludes the application's own startup |
| NFR-06 allocation count | Not measured: BenchmarkDotNet reports bytes, so "under 2 allocations beyond payload" is still open |
| NFR-05 PostgreSQL throughput (2k to 5k msg/s) | Measured 2026-11-16 on Docker Desktop (PostgreSQL 17 container, `PostgreSqlThroughputTests`): 20,000 messages in 16.39 s = 1,220 msg/s, every message handled once. **Below the target**; the Docker Desktop VM is a plausible cause, not confirmed. Judged on target hardware |
| Full requirements audit (2026-11-16, second pass) | [docs/messaging/brs-audit-2026-11-16.md](messaging/brs-audit-2026-11-16.md): every BRS requirement with its status and evidence |
| RabbitMQ throughput (`G2_measures_end_to_end_throughput_with_publisher_confirms`) | 23.4k and 23.3k msg/s; 25k target not met here |
| Coverage, `Modulus.Messaging` line coverage (CI gate, core unit suite, net10.0 only) | 87.2% (1,649 of 1,891 lines; floor 85%), measured 2026-11-16 |
| `tests/unit/Modulus.Analyzers.Tests` (MOD0001 to MOD0006) | 30 of 30, both TFMs (2026-11-16) |
| `tests/unit/Modulus.Cli.Tests` (`MessagingWiringTests`) | 7 of 7, net10.0 (2026-11-16): the `add-messaging` wiring |

Broker integration suites on Podman (2026-11-16, net10.0): RabbitMQ transport 28 of 28, PostgreSQL transport 35 of 35, Npgsql outbox 7 of 7, PostgreSQL sagas 5 of 5. The four restart tests that failed on Docker Desktop pass on Podman, which keeps the host port. Two regressions found and fixed earlier: the URN change made standalone transports unable to resolve URNs (the conformance suite failed), and sealed messages (the EncryptedPayload wrapper) and unregistered types were parked in `_error` instead of `_skipped`. The messaging integration project (`Modulus.Messaging.Integration.Tests`, 6 of 6) was last run before the per-bus wire-name change; the transport suites that exercise the send path were re-run after it.

Earlier integration results (2026-11-15, before the reconnect and request changes) are superseded by the table above.

## Requirements status

Status key: **Built** (code and tests), **Partial** (built with a stated limit), **Open** (not built, the reason is given), **Decided** (not built by decision, see Decisions).

### Publish, send, consume
- FR-01 to FR-03, FR-C1 to FR-C3, RULE-07, RULE-14: **Built.** Consumers, publish, send, per-message DI scope, settings with the narrowest scope winning. FR-03 `ConsumerDefinition<TConsumer>` (2026-11-16): a class holding the endpoint name and the consumer's settings, registered with `AddConsumer<TConsumer, TMessage>(definition)`; it is a thin layer over `AddConsumer` (`ConsumerDefinitionTests`). FR-C3 defaults are covered by `DefaultsTests`. FR-C4 (no cost when unused) is measured by `MessagingUnusedFeaturesBenchmarks`: a registered observer adds about 0.1 µs and 0.28 KB per send; a registered filter runs at every point, so it is in use.
- FR-14 ordering by partition key: **Built** (already, before 2026-11-16; an earlier review wrongly reported it missing). `[PartitionKey]` on a property, or the endpoint as one partition with `Ordered`; per-partition send order under concurrency 16 is tested in-memory (`ConcurrencyTests`), and on PostgreSQL and RabbitMQ in their integration suites (not run on 2026-11-16).
- RULE-10 message identity: **Built** (2026-11-16). `[MessageUrn("urn:...")]` on a message type sets its wire identity, so renaming the class keeps the identity of a released message; the URN goes on the wire in place of the .NET name (RabbitMQ and PostgreSQL headers, the encrypted-message header). Two types under one URN are refused at bus build. A type without the attribute gets a derived `urn:clr:{FullName}`, which still works but changes when the class is renamed: put the attribute on every released message. The assembly-qualified name is still accepted on receive, so messages already in flight during a deploy decode. The outbox writers store the same name (the URN, or the .NET name in legacy mode), so a pending row sends after a rename. The name a bus sends is chosen per bus (`UseMessageNameFormatter`; `UseLegacyWireTypeNames` is the one-release bridge), and the choice travels on each envelope (`MessageEnvelope.WireType`).
- RULE-11 versioned contracts: **Partial.** The mechanism is built: a new URN per version, and both versions are consumable at once (tested). `MessageUrnContract` fails the build when a recorded URN disappears, and the contract file is in source (`MessageUrnContractTests`). A new URN for a breaking change is still a convention the tool does not check.
- FR-04 batch consumers: **Built.** `AddBatchConsumer` (`BatchSize`, `BatchWindow`), grouped per tenant; a failed batch is retried as a whole, then each message follows the normal redelivery and park rules.
- FR-05 `Fault<T>`: **Built.** Published when a message is parked in `_error`; `IFault` stops fault-of-fault loops.
- FR-06 request/response: **Built.** `IRequestClient.GetResponse`, `ConsumeContext.RespondAsync`, `AddRequestClient<TResponse>()`. A consumer failure answers the requester at once with `RemoteRequestException`; a timeout raises `RequestTimeoutException`.
- FR-20 filters and observers: **Built.** `IMessageFilter` at send, publish and consume; `IMessageObserver` for start, stop, send, publish, receive, consume and faulted attempts. A throwing observer is logged and does not affect the message. Filters run for an outbox send only when the dispatcher hands the row to the transport.
- FR-20 transaction filter: **Built (recommended shape).** The consume transaction is the bus's own `IConsumeUnitOfWork` step, begun before the filters and committed after them. `MessageFilterContext.UnitOfWork` gives consume-point filters the attempt's transaction, so a filter joins it and must not commit it (`TransactionFilterTests`). A separately configurable transaction step is not built; that is a further choice.
- FR-22 body size limit: **Built.** `UseMaxMessageSize` (default 1 MiB of JSON); a larger message is refused at send with `MessageTooLargeException`. The default costs one extra serialisation per send, so NFR-06 needs re-measuring with it on. Re-measured 2026-11-16 with the default on: median 4.51 µs (see the verification table).
- FR-22 payload encryption: **Built.** AES-256-GCM over the JSON body of a `[EncryptedMessage]` type or an endpoint with `EncryptPayload`; associated data is the message id, endpoint, consumer and type. The key id is in the payload, not a header. Keys come from `IMessageEncryptionKeys` (`StaticMessageEncryptionKeys` reads base64 32-byte keys). Transport operations (skip, redeliver, error) take the sealed form, so a plaintext body is never written back.
- FR-23 topology: **Built.** `IMessagingTopology.DescribeTopology()`, `UseDeployOnly()`, `UseEndpointNameFormatter`.
- FR-23 CLI scaffolding: **Built.** `modulus add-messaging` adds the packages, the bus (with outbox and inbox on PostgreSQL) and the settings to an API host. Checked by generating an app and running it against PostgreSQL 17.
- FR-24 telemetry: **Built.** Producer and consumer spans parented through `mod-traceparent`; `messaging.system` names the transport (`rabbitmq`, `postgresql`, `in-memory`); meters `messaging.client.sent.messages`, `messaging.client.consumed.messages`, `messaging.process.duration`, `messaging.parked.messages`; a queue-depth gauge on PostgreSQL. Every log line of a delivery carries `TraceId`, `SpanId`, `MessageId`, `Endpoint`, `TenantId`.
- FR-24 health checks: **Built.** RabbitMQ is healthy only when a passive declare on `amq.direct` answers within 5 s; PostgreSQL when `SELECT 1` succeeds. In-memory always reports healthy (no broker): **Decided**.
- FR-25 test harness: **Built.** `VirtualClock`, `FaultInjector`, `SagaHarness<TSaga>`; a saga timeout fires on the virtual clock.
- NFR-12 `[Sensitive]`: **Built**, for the payload diagnostic that a parked message logs at Debug. Other log lines carry identifiers, endpoints and reasons only.

### Reliability
- FR-07 retry policies (Immediate, Interval, Intervals, Incremental, Exponential, `Ignore<TException>()`): **Built.**
- FR-08 delayed redelivery: **Built.** Delivery-count header; PostgreSQL uses a row with a future visibility time; RabbitMQ a TTL queue. Also `ConsumeContext.Redeliver(delay)` (2026-11-16): it throws `RedeliverMessageException`, so the attempt's work is rolled back; the message comes back after the delay; it is not a fault, does not feed the kill switch, and does not use the retry policy. A broad `catch (Exception)` in a consumer would turn the request into a failure. Bounded by `MaxDeliveryCount` (D3), since the policy counter is not advanced. Tests: `RedeliverTests`.
- FR-09 `_error` and `_skipped` with fault headers: **Built.**
- FR-09 replay by API: **Built.** `IMessageErrorQueue.ReplayAsync` (oldest first, counts reset) on all three transports (integration: `ErrorReplayTests`), and the operator endpoint `POST /messaging/errors/{endpoint}/replay?limit=` (1 to 1000, default 100), behind the Critical permission `messaging:errors:replay`.
- FR-09 replay by CLI: **Decided.** The `modulus` tool scaffolds code and never holds broker or database credentials. The operator endpoint is the route for the BRS "CLI" half.
- FR-10 outbox (RULE-01): **Built** on EF Core and Npgsql, and in memory (2026-11-16): `AddModulusMessagingInMemoryOutbox()` holds a scope's publishes and sends until `IInMemoryMessageOutbox.CommitAsync()`, sends each once, and drops them on rollback or when the scope ends uncommitted (`InMemoryOutboxTests`). Not durable: a process that exits between commit and send loses the pending messages; scheduling inside it is refused (`NotSupportedException`), not faked. Limit (EF Core and Npgsql): request/response replies are not captured by the outbox (RULE-09).
- FR-11 inbox (RULE-04): **Built** on EF Core and Npgsql; duplicates are skipped and counted on `messaging.inbox.duplicates`.
- FR-12 scheduling with cancel: **Built** on InMemory, PostgreSQL and the outbox. **Decided:** RabbitMQ cancel is not supported (the TTL-bucket scheduling cannot search by token). Scheduling through the outbox is cancelled by token.
- FR-13 TTL: **Built** on every transport (`mod-expires-at`, checked in the bus).
- FR-13 priority: **Built** on InMemory and PostgreSQL. **Decided:** RabbitMQ keeps quorum queues, which refuse `x-max-priority`, so RabbitMQ gives no priority.
- FR-14 ordering: **Built.** Within a host on all transports; across hosts on PostgreSQL (one row per partition in flight) and on RabbitMQ with the opt-in `SingleActiveConsumer` endpoint option.
- FR-15 flow control: **Built.** Rate limit per endpoint, kill switch (pauses an endpoint when the failure ratio trips), concurrency limit through `IConcurrentDeliveryTransport`.
- FR-16 consume timeout and graceful drain: **Built.** The stop timeout defaults to 30 s (NFR-18); unfinished messages go back to the broker or queue.
- FR-17 state-machine sagas: **Built.** `State`, `Event<T>().CorrelateBy`, `Initially`, `During`, `When(...).Then(...).TransitionTo(...)`, `Complete()`, `Ignore()`, `If`, `IfElse`, `Catch`. `Catch` discards the attempt's state and effects and runs the compensation on the original saga.
- FR-17 request step: **Built** (2026-11-16). `SagaContext.Request(address, message)` sends through `IBus.SendRequest`, which carries the request id and the saga's endpoint as the reply address. The answer arrives as the message the saga declares; a consumer failure arrives as the public `RequestFault`, declared as `Event<RequestFault>().CorrelateBy(f => f.RequestId)`. The request id is the saga's `CorrelationId`, so **one request is outstanding per saga instance**. Tested on in-memory, through the outbox, and on RabbitMQ and PostgreSQL (`RequestHeaderTests`).
- FR-17 request timeout: **Built.** `Request(address, request, timeout)` schedules a `SagaRequestTimedOut` to the saga's endpoint, with the timeout stored with the saga and cancelled when it completes. An answer or fault for the open request closes it; a timeout that no longer matches the open request is dropped, not parked. Covered for both saga styles (`SagaRequestTimeoutTests`).
- FR-17 completing cancels pending timeouts: **Built** (the RabbitMQ cancel is best effort, as above).
- FR-18 consumer sagas: **Built.**
- FR-19 saga repositories: **Built** on in-memory, EF Core and Npgsql JSONB, with optimistic concurrency. The Npgsql concurrency result is explained (see Production readiness). The repositories pass the shared conformance suite (NFR-16).
- FR-21 tenant context on the bus: **Built.** `MessageTenant.Enter`, `mod-tenant-id` on every message, `ConsumerOptions.RequireTenant` (parks with `no-tenant`).
- FR-21 tenant bridge to the framework: **Built.** `AddModulusTenantMessaging()` verifies the company of each message and refuses an unknown, inactive or malformed one (`tenant-refused`, parked at once, D8).
- FR-21 tenant-scoped saga keys: **Built.** `SagaKeys.ForTenant`.
- FR-21 row-level isolation: **Partial.** The inbox record has a nullable `TenantId`. On the PostgreSQL queue table, RLS is built and **opt-in** (`TenantRowLevelSecurity`, policy script `docs/messaging/postgresql-tenant-rls.sql`). It is off by default because the transport is host-level and isolation is enforced at consume time (D7).
- NFR-02 RabbitMQ reconnect with 1 s to 60 s back-off: **Built** (own reconnect loop; the client's automatic recovery is off). Tested by the broker-restart test, which passes on Podman, and by the back-off unit tests.
- NFR-02 PostgreSQL outage: **Built.** Sends fail, polling resumes after the database restarts (integration, Podman).
- NFR-02 on Docker Desktop: **Environment, not code.** The restart tests fail there because a restarted container gets a new host port. They pass on Podman, which keeps the port (2026-11-16).
- NFR-09 TLS: **Built** on RabbitMQ (AMQP TLS, `TlsCertificateValidation`) and PostgreSQL (`SSL Mode=VerifyFull`); both refuse an untrusted certificate.
- NFR-10 deserialization safety: **Built** (2026-11-16). A type name from the wire is resolved only against the registered message types, or concrete types assignable to them (`MessageTypeAllowList`, built lazily from the registered consumers). The RabbitMQ and PostgreSQL receive paths and the encrypted-message header use it; a name that is not allowed parks the message before its body is read. Limits: a transport used without a bus keeps the framework's name resolution (it has no registered types); the outbox rebuild (`Type.GetType` on rows this application wrote) is unchanged; a type whose assembly is never loaded in the process cannot be resolved. Tests: `MessageTypeAllowListTests` (unit); the broker paths are covered by the integration projects, which were not run on 2026-11-16 (no container runtime).
- NFR-13 native AOT: **Decided, reopened for measurement only** (2026-11-16). With `IsAotCompatible=true` the core `Modulus.Messaging` project has 14 distinct trimming and AOT warnings (IL2026, IL2057, IL2067, IL2070, IL2072, IL2075, IL2091, IL3050): reflection-based JSON serialization of message types, DI activation of consumers and filters, `Type.GetType` on names, and `[Sensitive]` property reads. The transports and the sagas package add more. Removing them needs source-generated serializer contexts from each application, which is the breaking change the decision names; the requirement's "Must" priority conflicts with that, and the owner has not reopened the decision. The original reason still holds: the bus serialises arbitrary message types by reflection; AOT would need a serializer context from every app, which is a breaking change.
- NFR-16 coverage: **Built** as a CI gate (`build/check-messaging-coverage.py`, 85% for `Modulus.Messaging`, measured 87.2% on net10.0 on 2026-11-16). The saga repositories are covered by the shared conformance suite (`SagaRepositoryConformance`).
- NFR-17 telemetry conventions: **Built** (see FR-24).
- NFR-19 migration guide: **Built**, `docs/messaging/migration-from-masstransit.md`.
- NFR-20 docs site: **Partial** (2026-11-16). Written: a quick start per transport (`docs/messaging/quick-start.md`), a configuration reference of every setting and its default (`docs/messaging/configuration-reference.md`), and the migration guide. The in-memory parts of the quick start are compiled and run by `QuickStartDocTests`; the broker parts are not run. **Open:** a published site (needs a hosting decision), and the samples (`samples/` needs the owner's permission).
- NFR-21 signed packages: **Open** (owner's certificate, F-25).

### Sagas and tenants: limits
- A saga has one outstanding request per instance (see FR-17).
- The derived saga key uses the tenant string as the header carries it; producers must send one consistent form per tenant (the adapter sends the id in `N` form).
- A consumer whose company is not entered runs in whatever `ICurrentTenant` reports. Register `AddModulusTenantMessaging()` for company consumers.

## Decisions

These are the owner decisions recorded in this plan. Each one states its current status.

- **D1 RabbitMQ priority:** keep quorum queues. No priority on RabbitMQ (FR-13). Priority-sensitive work needs its own endpoint.
- **D2 RabbitMQ scheduled cancel:** stays unsupported; cancel through the outbox or PostgreSQL (FR-12).
- **D3 Delivery-count limit:** built. `ConsumerOptions.MaxDeliveryCount` (default 20); a delivery past it is parked with reason `delivery-limit` without running the consumer.
- **D4 Commits:** the work is on `feat/messaging-hardening`. The commit history is not rewritten. The work of the 2026-11-16 closing pass (the outbox, saga, URN, analyzer and benchmark changes listed under Production readiness) is not committed yet.
- **D5 Saga `If` and `Catch`:** built. A false `If` skips its block and accepts the event; `Catch` discards the attempt and runs the compensation.
- **D6 Payload encryption:** built, with the deviations listed under FR-22.
- **D7 Row-level isolation:** the inbox has a `TenantId` column (built). RLS on the queue table is built and opt-in; it is off by default.
- **D8 Refused company:** parked at once with reason `tenant-refused`. A transient tenant-store failure still retries.
- **D9 `messaging.system`:** built (the transport name), with a log scope per delivery and the queue-depth gauge.
- **D10 CLI scaffolding:** built (`modulus add-messaging`). The topology API and deploy-only mode followed.
- **D11 Circuit breaker:** built after all (`ConsumerOptions.CircuitBreaker`, 3 unit tests). The kill switch remains the BRS criterion.
- **D12 RabbitMQ `MaxQueueLength` on existing queues:** queues keep their arguments, so set it before the queue is created, or apply a RabbitMQ policy (`rabbitmqctl set_policy`).
- **D13 In-memory health:** always healthy (no broker), and the description says so.
- **D14 Clock skew:** accepted and documented. Relative delays use the database clock; an absolute `deliverAt` uses the caller's clock.
- **D15 Hardware, chaos, Docker Desktop, samples, G4:** blocked on the environment or the owner. See Open tasks.

## Open tasks

Grouped by what unblocks them. Items marked done are kept at the end, so the history stays readable.

### Needs the owner
1. **Code-signing certificate and signed packages (F-25, NFR-21).** Needed before any package is released.
2. **Release gate G4 (production).** One product in production for two weeks.
3. **API baseline.** The new public surface is in the `PublicAPI.Unshipped.txt` files: the `MessageEnvelope` constructor gained `WireType`, `IRequestResponse` gained `RequestId`, and the saga request timeout and the wire-name builder methods are new. Moving them to `Shipped` is a release step.
4. **Package validation.** `EnablePackageValidation` is off for the new messaging packages, because nothing of their names is published yet. Turn it back on after each first release.
5. **Rolling-upgrade bridge.** `UseLegacyWireTypeNames()` is for one release. Remove it once every consumer reads URNs; that is a release-plan decision.
6. **Commit.** The work of this pass is uncommitted, on top of `dd44d94d` on `feat/messaging-hardening`. Decide whether to commit and how to split it.

### Needs target hardware
7. **G2 throughput, 25k msg/s with confirms (NFR-04).** Not reached on the development machine (23.4k msg/s). Measure on the target hardware.
8. **Bus overhead on the target hardware (NFR-06).** Measured here at a 4.51 µs median (in-memory, 2026-11-16). Measure again on the target.
9. **PostgreSQL throughput and index tuning (NFR-05).** Measured at 1,220 msg/s on Docker Desktop; measure on the target.

### Needs `samples/` (the owner must allow it)
10. **G0 consumer-shape generator.**
11. **G3 TradeFlow sample on PostgreSQL** (orders to billing).

### Needs a dedicated runner or the environment
12. **NFR-06 regression gate on a dedicated runner.** `.github/workflows/benchmark.yml` runs on demand, and `build/check-benchmark-regression.py` fails above 10% over `build/benchmark-baseline.json`. Shared runners are too noisy for it, so it is not a pull-request check.
13. **NFR-06 allocation count.** BenchmarkDotNet reports bytes, not allocation counts; the "under 2 allocations beyond payload" target needs an allocation counter.
14. **Docker Desktop restart tests.** They fail on Docker Desktop because the host port changes. They pass on Podman; run them on Podman or on a Docker host that keeps the port.

### Engineering
15. **Analyzer in a consumer build.** MOD0006 and its props file are packed and tested; a consumer project with `ModulusRequireMessageUrn` set was not built here. Run one before the first release.
16. **FR-20 separately configurable transaction step.** Not built; the shape chosen is the bus-owned transaction exposed to filters (see Requirements status).
17. **Test-helper race in the RabbitMQ integration tests.** A basic-get on a queue not yet declared closes the channel. Fixed in the poll helper; keep an eye on it.
18. **PostgreSQL integration tests run one at a time.** A purge test removes every parked row, so the assembly runs serially. Revisit if the suite gets slow.
19. **Messaging integration project and Chaos run.** `Modulus.Messaging.Integration.Tests` (6 of 6) and the Chaos G1 run predate the per-bus wire-name change; re-run both before a release.

### Done in this pass (kept for history)
- Npgsql saga repository, unexplained result (earlier item 14): explained, see Production readiness.
- Partial schedule on a failed multi-envelope publish (earlier item 15): cancelled on failure, tested.
- FR-17 saga request timeout (earlier item 13): built, tested.
- The Npgsql, PostgreSQL and RabbitMQ runs listed as not yet done, and the Docker Desktop restart tests (earlier items 11 and 12): run on Podman, all pass.

### Decided not to build (listed for completeness, reopen only with a decision)
- RabbitMQ priority (D1) and RabbitMQ cancel of scheduled messages (D2): broker limits.
- The CLI form of replay (FR-09): see the Requirements status.
- Native AOT (NFR-13).
- A circuit breaker separate from the kill switch: built as an option (D11), so this is no longer open.
- An in-memory health signal (D13).

## Production readiness (2026-11-16)

What blocks a production release, or remains to be done before one. The status of each requirement is in the
[BRS audit](messaging/brs-audit-2026-11-16.md).

### Blocked on the owner or the environment
- **Code-signing certificate (NFR-21, F-25):** no package can be released without it.
- **Release gate G4:** a real product runs on the bus in production for two weeks.
- **Target hardware for throughput:** RabbitMQ 25k msg/s (G2; 23.4k here), in-memory 1M msg/s on 8 cores (NFR-03; 564k on 12 threads), PostgreSQL 2k to 5k msg/s (NFR-05; 1,220 on Docker Desktop, where the VM overhead is a likely cause).
- **Outage tests on a proper host:** done on Podman (2026-11-16): the four restart tests pass. Docker Desktop still fails them because the port changes.
- **Runs:** the Sagas PostgreSQL and Npgsql outbox/inbox integration projects and the messaging CLI wiring suite were run and pass (2026-11-16). The messaging integration project and the Chaos run predate the per-bus wire-name change; re-run them before a release.
- **`samples/` folder:** needs the owner's permission. It blocks the G0 consumer-shape generator and the G3 TradeFlow sample.

### Must fix before production (engineering)
Status as of 2026-11-16, verified by tests (unit on net10.0; integration on Podman).
- **Outbox stores the .NET type name.** *Done:* the EF and Npgsql writers store the URN; dispatchers resolve it. Legacy assembly-qualified rows still dispatch (`TransactionalOutboxTests`). The dispatchers pass no allow-list, so a URN resolves to any loaded class carrying it; rows are written by the app itself.
- **Rolling upgrade across the URN change.** *Done, per bus (MassTransit-style):* `MessagingBusBuilder.UseMessageNameFormatter(Func<Type,string>)` chooses the name each message is sent under; the default is the URN. `UseLegacyWireTypeNames()` is the one-release bridge that sends the .NET name. The choice travels on each `MessageEnvelope.WireType`, so the RabbitMQ and PostgreSQL codecs, both outboxes and encrypted-message headers all use it, and two buses in one process can differ (`LegacyWireTypeNameTests`). Receivers accept both forms. Remove the legacy bridge once every consumer reads URNs. Nothing has been released, so there is no mixed fleet yet.
- **Npgsql saga repository (item 14).** *Explained:* the current repository passes 10 of 10 repeats at 7 messages and the 50-message test. An unconditional-upsert variant loses updates (count 1 instead of 7), which is the failure the version check prevents. The exact "0" from the earlier run was not reproduced.
- **Partial schedule on a failed multi-envelope publish (item 15).** *Done:* the outbox scheduler cancels rows it already wrote when a later write fails; the EF outbox detaches unsaved rows on cancel (`OutboxScheduleRollbackTests`, `TransactionalOutboxTests`).
- **RULE-06 saga atomicity.** *Done:* the Npgsql saga repository now runs on the scope's unit of work, so saga state commits or rolls back with the inbox and outgoing rows. This was a real gap: it wrote outside the transaction. Covered by `RULE_06_*` PostgreSQL tests. The repository is now registered scoped.
- **FR-17 saga request timeout.** *Done:* `SagaContext.Request(address, request, timeout)` schedules a `SagaRequestTimedOut` to the saga's endpoint. An answer or fault for the open request closes it; a stale timeout is dropped. Tests in `SagaRequestTimeoutTests` (both saga styles).
- **FR-20 transaction filter.** *Done (recommended shape):* the consume transaction stays the bus's own `IConsumeUnitOfWork` step, begun before the filters and committed after them. `MessageFilterContext.UnitOfWork` exposes it to consume-point filters, so a filter joins the consumer's transaction and must not commit it (`TransactionFilterTests`). A separately configurable transaction step was not built; say if you want one.
- **Unattributed message types.** *Done (opt-in):* analyzer MOD0006 reports a message type that is sent, published or scheduled from source without `[MessageUrn]`. It runs only in projects that set `<ModulusRequireMessageUrn>true</ModulusRequireMessageUrn>`, so existing projects are unaffected (`MessageUrnAnalyzerTests`). It ships in the `Synvro.Modulus.AspNetCore` package, which also imports `build/Synvro.Modulus.AspNetCore.props` to make the property visible to the analyzer. A messaging-only project must reference that package to get the analyzer. The package builds and contains both files; a consumer build with the property set was not run here. The RULE-11 contract check also catches a rename at CI time.
- **RULE-11 in CI.** *Done:* `MessageUrnContract` fails when a recorded URN disappears; the contract file is `tests/unit/Modulus.Messaging.Tests/message-urns.contract`.
- **FR-C3 defaults and FR-C4 overhead.** *Done:* `DefaultsTests` covers FR-C3. `MessagingUnusedFeaturesBenchmarks` measures FR-C4: a registered observer adds about 0.1 µs and 0.28 KB per send; the no-observer path allocates less after a guard was added. A registered filter runs at every point, so it is a feature in use, not an unused one.
- **Saga repository conformance (NFR-16).** *Done:* `SagaRepositoryConformance` (seven checks) runs against the in-memory, EF/SQLite and PostgreSQL repositories. It found an EF defect (a duplicate insert threw a tracking error instead of a concurrency conflict), now fixed.

### Decided not to build; review before production
- **Native AOT (NFR-13).** 14 trimming warnings in the core; the decision stands unless the owner reopens it.
- **CLI replay (FR-09).** The operator endpoint is the only route.
- **RabbitMQ priority (FR-13) and RabbitMQ scheduled cancel (FR-12).** Not supported by quorum queues and the TTL-bucket scheduling.
- **Row-level isolation (FR-21).** PostgreSQL RLS is built but off by default.

### Operations and release
- **Monitoring (done, 2026-11-17):** alert rules in `docs/messaging/monitoring/prometheus-alerts.yml` (parked messages, burst, slow consumers, throughput stopped, redeliveries piling, queue depth) and a dashboard in `docs/messaging/monitoring/grafana-dashboard.json`. Metric names follow the OpenTelemetry-to-Prometheus mapping; check them against the exporter once before loading (the file says how).
- **Runbook (done, 2026-11-17):** `docs/messaging/operations.md` covers the signals, the parked-message procedure and the operator replay endpoint, outages, scheduled-message cancel, the rolling upgrade, the schema rollout and health.
- **Schema rollout (done, 2026-11-17):** the queue schema script carries a version header, and `SchemaScriptTests.The_schema_script_is_additive_only` fails the build if it drops, renames or retypes anything. The rule is in the runbook.
- **Public API:** move `PublicAPI.Unshipped.txt` to Shipped at the first release, and turn `EnablePackageValidation` on then.
- **Docs site (NFR-20):** quick start, configuration reference, migration guide and the runbook are written; there is no hosting decision (owner).
- **Allocation count (NFR-06):** time is measured; allocations per message are not counted (bytes only).

## Known limits

- In-memory messages are lost if the process exits (no durability). It cannot pass G1 on its own.
- The in-memory outbox (FR-10, 2026-11-16) is not durable either: messages pending a commit are lost if the process exits, and scheduling inside it is refused.
- `ConsumeContext.Redeliver(delay)` (FR-08) does not advance the retry policy, so a consumer that keeps asking for redelivery is bounded only by `MaxDeliveryCount` (D3).
- Types with no `[MessageUrn]` keep a name-derived URN (RULE-10): renaming such a class changes its identity. Analyzer MOD0006 reports it in projects that set `ModulusRequireMessageUrn`.
- A standalone transport (no bus) resolves URNs by scanning loaded assemblies, once per URN; a bus-hosted transport resolves them only through the registered message types.
- In-memory delayed redeliveries live in a timer task; a stop before the delay ends loses them.
- A stop with the RabbitMQ connection already down cannot requeue in-flight messages from this process. The broker requeues them when the connection drops, so no message is lost, but the handler's attempt is repeated.
- On PostgreSQL the due time of a schedule is the caller's timestamp, compared with the database clock (D14).
- Filters and observers do not run for an outbox send until the dispatcher hands the row to the transport.
- Ack after commit (RULE-02) holds on RabbitMQ and in the EF outbox and inbox transactions. The InMemory transport has no ack, so a message it has handed over is gone if the process exits.
- The default 1 MiB size limit adds one serialisation per send.
- `Modulus.Messaging` depends on `Microsoft.Extensions.DependencyInjection.Abstractions` and `Hosting.Abstractions` (Microsoft packages, the repo convention). No third-party packages.

## Verification commands

- Build: `dotnet build modulus.slnx` with 0 warnings.
- Unit: `dotnet test modulus.slnx --filter "Category=Unit"`.
- Format: `dotnet format modulus.slnx --verify-no-changes`.
- Integration, one project at a time, on Podman:
  `DOCKER_HOST=unix:///run/user/$UID/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true dotnet test tests/integration/<Project> -p:ModulusTestTfm=net10.0 --filter "Category=Integration"`.
- Chaos (long): `dotnet test tests/integration/Modulus.Messaging.Chaos.Tests --filter "Category=Chaos"`; the size and pace come from `MODULUS_CHAOS_*` variables.
- Benchmark: `dotnet run -c Release --project tests/benchmarks/Modulus.Benchmarks -- --filter "*MessagingBenchmarks*"`, then `python3 build/check-benchmark-regression.py BenchmarkDotNet.Artifacts`.
- Coverage gate: `python3 build/check-messaging-coverage.py artifacts/coverage 85` after a coverage run.
- Public API: the analyzers (RS0016) fail the build when an entry is missing from `PublicAPI.Unshipped.txt`.

## Commits (feat/messaging-hardening)

The 2026-11-16 closing pass is uncommitted; the list below ends at the last commit before it.

- `cd92c113` close the remaining v1 requirements (gap-closure pass)
- `373eb4ba` RabbitMQ reconnects with a 1 s to 60 s back-off (NFR-02)
- `d2f570ce` operator endpoint to replay parked messages (FR-09)
- `93af1505` coverage floor on the core package (NFR-16)
- `3c42b55e` bus overhead benchmark and plan record (NFR-06)
- `4f83a64c` PostgreSQL migration script (later removed as a duplicate, see below)
- `a1477ae1` saga request step with fault correlation (FR-17, FR-06)
- `dfc5b637` manual regression gate for the bus overhead (NFR-06)
- `dd44d94d` request-header and fault tests on RabbitMQ and PostgreSQL

The migration script is `docs/messaging/postgresql-queue-schema.sql`, checked by `SchemaScriptTests`. An earlier duplicate (`postgresql-schema.sql` and its test) was removed in this pass, because it was identical.
