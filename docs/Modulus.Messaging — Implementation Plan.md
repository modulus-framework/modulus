# Modulus.Messaging: implementation plan

Source requirements: [BRS](Cobytelabs.Messaging%20—%20Business%20Requirements%20Specification%20(BRS).md) (v1.0 draft). The bus is named **Modulus.Messaging** (working name; the BRS open issue on the final name stays open).

## Decisions

- Built inside this repo under `src/messaging/`, one project per concern, like `Modulus.Mediator`.
- Full v1 scope (BRS section 4): publish/send, consumers, retry, error and skipped queues, outbox, inbox, sagas (state machine and Rebus adapter), scheduling, flow control, transports InMemory, RabbitMQ, PostgreSQL, multi-tenancy, encryption, OpenTelemetry, health, test harness.
- Reuse and move existing code: outbox row and processor (`Modulus.Outbox`, `Modulus.Outbox.Abstractions`), inbox (`Modulus.Inbox`), RabbitMQ connection (`Modulus.EventBus.RabbitMQ`), Rebus saga wiring (`Modulus.Sagas`). Old packages stay as thin adapters with unchanged public API.
- Optional Rebus saga support for developers (`Modulus.Messaging.Sagas.Rebus`).
- Conventions from AGENTS.md: `net8.0;net10.0`, CPM, `TreatWarningsAsErrors`, PublicAPI files, zero third-party dependencies in core, `Category=Unit` / `Category=Integration`.

## Projects

| Project | Purpose | Phase |
|---|---|---|
| `Modulus.Messaging` | Core abstractions and pipeline | M0 |
| `Modulus.Messaging.Transports.InMemory` | Reference transport | M0 |
| `Modulus.Messaging.Testing` | Harness, virtual clock, conformance kit | M0 |
| `Modulus.Messaging.Transports.RabbitMQ` | AMQP transport | M2 |
| `Modulus.Messaging.Transports.PostgreSql` | Queue table transport | M3 |
| `Modulus.Messaging.Outbox.EntityFrameworkCore`, `.Outbox.Npgsql` | Transactional outbox | M1 |
| `Modulus.Messaging.Inbox.EntityFrameworkCore`, `.Inbox.Npgsql` | Idempotent inbox | M1 |
| `Modulus.Messaging.Sagas` | State machine sagas, repositories | M3 |
| `Modulus.Messaging.Sagas.Rebus` | Optional Rebus saga adapter | M3 |
| `Modulus.Messaging.Modulus` | Modulus host integration | M4 |

## Phases and gates (BRS section 13)

**M0 Foundations, weeks 1 to 4, gate G0.** Core abstractions (`IBus`, `IPublishEndpoint`, `ISendEndpointProvider`, `IConsumer<T>`), InMemory transport, publish and send, DI scope per message (RULE-07), `ConsumerDefinition<T>`, settings with narrowest scope wins (FR-C1 to FR-C3, RULE-14), URN registry (RULE-10), conformance suite skeleton. Gate: conformance green on InMemory.

**M1 Reliability, weeks 5 to 8, gate G1.** Retry policies (FR-07), delayed redelivery (FR-08), `_error` and `_skipped` with fault headers and replay (FR-09, RULE-03), `Fault<T>` (FR-05), outbox on EF Core and Npgsql (FR-10, RULE-01), inbox (FR-11, RULE-04), ack after commit (RULE-02), kill switch, circuit breaker, consume timeout, graceful drain (FR-15, FR-16). Gate: chaos run of 1M messages with random kills, zero lost, zero double-applied.

**M2 RabbitMQ, weeks 9 to 12, gate G2.** Transport with confirms and quorum queues, DLX to `_error`, delayed-message plugin with TTL fallback, reconnect backoff (NFR-02). Gate: 25k msg/s with confirms, overhead under 5 µs p50.

**M3 PostgreSQL, weeks 13 to 17, gate G3.** PostgreSQL transport, scheduler with cancel (FR-12), TTL (FR-13), FIFO partitioning (FR-14), state-machine and consumer sagas (FR-17, FR-18, FR-19, RULE-06), Rebus saga adapter, tenant context with RLS (FR-21). Gate: TradeFlow sample on PostgreSQL.

**M4 Release, weeks 18 to 20, gate G4.** Payload encryption (FR-22), `[Sensitive]` redaction (NFR-12), OpenTelemetry conventions (FR-24), health checks, Modulus host adapter and CLI scaffolding (FR-23), migration guide, API baseline frozen, signed packages. Gate: one product in production for two weeks.

## Verification

- Every phase: `dotnet build modulus.slnx` with 0 warnings, `dotnet test modulus.slnx --filter "Category=Unit"`, `dotnet format modulus.slnx --verify-no-changes`.
- Tests are named with the BRS ID (e.g. `FR_09_poison_message_lands_in_error`).
- Integration: `dotnet test tests/integration/Modulus.Messaging.Integration.Tests -p:ModulusTestTfm=net10.0` (Docker or Podman).
- Old packages' unit tests pass after each move.
- No commits unless asked.

## Status

As of 2026-11-15 (fourth pass). M0 done: core API, in-memory transport, harness, conformance suite (run on InMemory, PostgreSQL and RabbitMQ). M1 mostly done: outbox and inbox on EF Core, retries, redelivery, error and skipped queues, faults, consume timeout, drain, rate limit, kill switch, concurrency limit. Open in M1: circuit breaker (optional), chaos run (G1). M2 RabbitMQ transport built and integration-tested: concurrency, TLS, scheduling through TTL buckets, bounded queues with publisher-confirm refusal; restart tests fail on Docker Desktop. M3 mostly built: PostgreSQL transport (concurrency, purge, migration script, auto-purge), scheduler with cancel (PostgreSQL, in-memory, outbox; not RabbitMQ), TTL, priority (not on RabbitMQ, by decision), ordered processing by partition, sagas: state machine (FR-17, core), consumer sagas (FR-18), and repositories on in-memory, EF Core and Npgsql JSONB with optimistic concurrency (FR-19). Tenant context (FR-21) is partly built on the bus (2026-11-15, see Progress); the database-level part is not. Not started: TradeFlow sample (G3). M4 started: tracing, health checks, metrics, structured logs, the host adapter (`Modulus.Messaging.Modulus`) and the tenant bridge are built; encryption, `[Sensitive]` redaction, CLI scaffolding and the migration guide are not (see Decisions for the 2026-11-15 plan).

### Still open

- **FR-14, "under concurrency 16":** done on all three transports. Within a host, RabbitMQ and in-memory keep each partition in order under concurrency, and PostgreSQL claims one row per partition across hosts. Across hosts, RabbitMQ keeps partition order only with the opt-in `SingleActiveConsumer` endpoint (2026-11-15, plan step 10); without it, competing consumers do not enforce partition order.
- **Sagas, M3:** state-machine sagas (FR-17) core, consumer sagas (FR-18), and the three repositories (FR-19) are built. Open parts are listed under Open tasks.
- **Tenant context, M3:** bus-level tenant and the `ICurrentTenant` bridge are built (FR-21, 2026-11-15); the row-level part (RLS, tenant-stamped outbox and inbox rows, saga keys) is not. See Progress.
- **TradeFlow sample, M3 gate G3:** orders to billing on PostgreSQL. Not started. The sample folder is off-limits until the owner allows it.

### Open tasks

Grouped by what unblocks them. Each item names the requirement it closes.

**Decided 2026-11-15 (the decisions table below; the owner asked for these to be decided and recorded)**
- D1 RabbitMQ priority: keep quorum queues, no priority (FR-13).
- D2 RabbitMQ cancel of a scheduled message: stays unsupported; cancel is through the outbox or PostgreSQL (FR-12).
- D3 Per-endpoint delivery-count limit: built 2026-11-15. `ConsumerOptions.MaxDeliveryCount` (default 20); a delivery past it is parked with reason `delivery-limit` without running the consumer. The count is the transport's (PostgreSQL `delivery_count`, RabbitMQ quorum `x-delivery-count`) or the bus's own redeliveries plus one, whichever is higher, so in-memory needs no counter of its own. Tests: 2 core unit, 1 PostgreSQL and 1 RabbitMQ integration.
- D4 Commit: the work is on `feat/messaging-hardening`. It landed as one commit (`cd92c113`) rather than the three planned; the split was not redone, since the history is already shared-safe and rewriting it adds risk for no gain. The CLI changes outside this work stay uncommitted.
- D5 Saga `If` and `Catch`: built 2026-11-15. `If` false skips the guarded steps and accepts the event; `Catch` discards the attempt's state and effects and runs a compensating reaction on the original saga.
- D6 Payload encryption (FR-22): built 2026-11-15. Bus-level AES-256-GCM over the JSON body of a `[EncryptedMessage]` type or an endpoint with `EncryptPayload`, with the message id, endpoint, consumer and type as associated data. Deviations from the plan: the key id is inside the payload rather than a `mod-key-id` header, and there is no size limit, because the bus has none to check. Keys come from `IMessageEncryptionKeys` (`StaticMessageEncryptionKeys` reads base64 32-byte keys). Transport operations (skip, redeliver, error) always take the sealed form, so a plaintext body is never written back. A fault of a sealed message is sealed too.
- D7 RLS on the queue table: not done (the transport is host-level by design); the inbox record gets a nullable `TenantId`, built 2026-11-15 (upgrade: a migration).
- D8 Refused company: parked at once with reason `tenant-refused` (built 2026-11-15: `MessageParkException` in core; the company adapter throws it for an unknown or inactive company, and a transient store failure still retries; `D8_a_refused_company_is_parked_at_once_and_is_not_retried`).
- D9 `messaging.system`: the transport name (`rabbitmq`, `postgresql`, `in-memory`), built 2026-11-15, with a log scope per delivery and the queue-depth gauge.
- D10 FR-23 CLI scaffolding: superseded 2026-11-15 by the owner's request to build it now (`modulus add-messaging`, see Progress). The v1 topology API and deploy-only mode followed on 2026-11-16.
- D11 Circuit breaker: not built; the kill switch covers the criterion.
- D12 RabbitMQ `MaxQueueLength` on existing queues: apply a RabbitMQ policy (`rabbitmqctl set_policy`) rather than a migration step.
- D13 In-memory health: always healthy is correct (no broker); documented.
- D14 Clock skew: accepted and documented; relative delays use the database clock, only an absolute schedule time uses the caller's clock.
- D15 Hardware, chaos run, Docker Desktop, G0/G3 samples and G4: blocked on environment or the owner's permission; no work scheduled.

**Needs permission or an environment change**
- Consumer-shape generator for the M0 gate (G0): needs the samples, which are off-limits until allowed.
- TradeFlow sample on PostgreSQL (G3): needs the sample folder.
- Restart tests on Docker Desktop, both transports (NFR-02): the restarted container gets a new host port, the cause is not confirmed. Run on Podman or a clean Docker host.
- Latency at 5 µs p50 and throughput at 25k msg/s with confirms (NFR-06, NFR-04, gate G2): measured on this development machine on 2026-11-16 (see Progress). The target hardware is still not available, so G2 is not judged here, and the CI regression gate is still open.
- PostgreSQL index tuning for 25k msg/s and the throughput measurement: need the target hardware.
- Chaos run of 1M messages with random kills, zero lost and zero double-applied (NFR-01, gate G1): passed on 2026-11-15 and again on 2026-11-16 (see Progress).

**Engineering, next**
- Sagas, FR-17: the `Request` step is built (2026-11-16). `SagaContext.Request(address, message)` sends through the new `IBus.SendRequest`, which carries the request id and the saga's endpoint as the reply address. The answer comes back as the message the saga declares; a consumer failure comes back as a public `RequestFault`, which the saga declares as `Event<RequestFault>().CorrelateBy(f => f.RequestId)`. The request id is the saga's `CorrelationId`, so one request is outstanding per saga instance. Tests: `SagaRequestStepTests` (fault and answer, net8 and net10) and `TransactionalOutboxTests.A_saga_request_sent_through_the_outbox_keeps_its_request_id_and_reply_endpoint`. Not covered: a timeout on the request (use `Schedule`), and a RabbitMQ or PostgreSQL integration test of the request headers (the headers are plain envelope headers, so they travel the same way on every transport). `If`, `IfElse`, `Catch` and `Ignore` are done. Decided (D5): a false `If` guard accepts the event and skips the guarded steps; a caught exception runs the `Catch` reaction on the original saga, and the attempt's effects, including outbox rows, are discarded.
- Sagas: cancel a saga's pending timeout when it completes: done 2026-11-15 (RabbitMQ still cannot cancel, see its scheduling decision).
- RabbitMQ: cross-host partition order is opt-in through `SingleActiveConsumer` (2026-11-15). Without it, order holds per queue only.
- Circuit breaker as a separate mechanism (optional: the kill switch covers the BRS criterion).
- RabbitMQ `MaxQueueLength` (done, off by default): existing queues keep their arguments, so a migration step is needed before a deployment turns it on.

**Larger, not started**
- Tenant context, FR-21: the BRS acceptance "a consumer for tenant A cannot read tenant B rows" is met for module EF data, through the framework's tenant filter (end-to-end test). Built since: the inbox record's nullable `TenantId` (D7). Not built by decision: RLS on the PostgreSQL transport's queue table, since the transport is host-level by design and isolation is enforced at consume time. The outbox row already carries the tenant in its headers.
- M4 release (gate G4, needs two weeks in production): payload encryption (FR-22, built), `[Sensitive]` redaction (NFR-12, built 2026-11-16), OpenTelemetry messaging conventions (FR-24, NFR-17, built), health checks (built), host adapter (built), CLI scaffolding (FR-23, built), migration guide from MassTransit v8 (NFR-19, written: `docs/messaging/migration-from-masstransit.md`), API baseline (new v1 surface is in PublicAPI.Unshipped until the release) and signed packages (not done, needs the owner's certificate).
- Test coverage target of 85% on core (NFR-16): measured 2026-11-15 from the unit suites on net10, merged across suites: `Modulus.Messaging` 88.2% of lines (896 of 1016), Sagas 87.2%, EF inbox 80.2%, EF outbox 67.0%, host adapter 64.0%. Integration suites are not in the figure. Gate added 2026-11-16: `build/check-messaging-coverage.py` runs in CI after the unit tests and fails below 85% for `Modulus.Messaging` itself (the sub-packages are not counted). Measured that day on the core unit suite alone, net8 and net10 merged: 86.0% (1388 of 1614 lines).
- Outbox and inbox on Npgsql: built (`Modulus.Messaging.Outbox.Npgsql`, `Modulus.Messaging.Inbox.Npgsql`; 7 integration tests). Superseded: this line was stale.

### V1 requirements, status after the gap-closure pass (2026-11-16)

Every BRS v1 requirement (Must and Should, plus the Could items FR-26 to FR-33, which the plan puts outside v1) was checked against the code and the tests. Status:

**Built and tested in this pass**
- FR-04 batch consumers: `AddBatchConsumer` with `BatchSize` and `BatchWindow`; batches are grouped per tenant; a failed batch is retried as a whole, then each message follows the normal redelivery and park rules (`V1GapTests.FR_04`).
- FR-06 remote faults: a failed request answers the requester at once with `RemoteRequestException` (the exception type, or the park reason such as `unhandled`, `expired`, `delivery-limit`); no timeout wait (`V1GapTests.FR_06`).
- FR-09 replay: `IMessageErrorQueue.ReplayAsync` moves `_error` messages back to their endpoint, oldest first, with delivery counts reset. Transports: in-memory (unit), PostgreSQL and RabbitMQ (integration: `ErrorReplayTests` in each project). The CLI part of the BRS acceptance is not built (see Not built).
- FR-20 filters and observers: `IMessageFilter` at send, publish and consume (resolved from the container per message; can refuse a message); `IMessageObserver` for bus start and stop, send, publish, receive, consume and faulted attempts. An observer that throws is logged and does not affect the message (`V1GapTests.FR_20`).
- FR-22 body size limit: `UseMaxMessageSize` (default 1 MiB, JSON bytes); a larger message is refused at send with `MessageTooLargeException`, before the outbox or transport sees it (`V1GapTests.FR_22`).
- FR-23 topology: `IMessagingTopology.DescribeTopology()` (endpoints, consumers, message types, `_error` and `_skipped` names); `UseDeployOnly()` declares the topology and receives nothing; `UseEndpointNameFormatter` renames derived endpoint names (`V1GapTests.FR_23`).
- FR-25 test harness: a `VirtualClock` (the bus, the in-memory transport, redelivery and sagas use the injected `TimeProvider`), `FaultInjector` (fails the next N consume attempts through the normal retry and park rules), and `SagaHarness<TSaga>` (find, wait for a state, wait for completion). A saga's payment-window timeout fires on the virtual clock (`SagaVirtualClockTests`).
- NFR-12 `[Sensitive]`: properties marked `[Sensitive]` show as `***` in the payload diagnostic that a parked message logs (Debug level). Other values are logged. The earlier "no content in logs" test now marks its secret text `[Sensitive]` (`V1GapTests.NFR_12`).

**Built earlier and re-verified**
- FR-17 `Request` (see Progress above), FR-21 row-level isolation, FR-14 single-active consumer, FR-12 scheduling with cancel (not on RabbitMQ, D2), FR-13 TTL and priority (not priority on RabbitMQ, D1), FR-24 telemetry and health, FR-05 faults, FR-18 consumer sagas, FR-19 saga repositories.

**Not built, with the reason**
- NFR-02 reconnect backoff from 1 s to 60 s: built 2026-11-16. The transport turns off the client's fixed-interval automatic recovery and runs its own loop: it tears the connection down, reconnects, and re-creates the publish channel, every consumer and the schedule consumer. The wait starts at `ReconnectMinDelay` (1 s), doubles per failed attempt and stops at `ReconnectMaxDelay` (60 s). Tests: `RabbitMqReconnectBackoffTests` (schedule, overflow, option validation) and `NFR_02_the_bus_recovers_when_the_broker_restarts_without_a_host_restart` (passes on Podman).
- NFR-13 Native AOT and trimming: decided 2026-11-16, not built. The bus serialises arbitrary message types with `System.Text.Json` reflection, and it cannot generate a `JsonSerializerContext` for types it has not seen. The messaging packages are not marked `IsAotCompatible`, and the same reason keeps the mediator and EF Core out of AOT (see the framework's AOT decision). Supporting it would mean every app passes its own serializer context, which is a breaking API change; that is not worth it for this release.
- FR-09 replay by API: built 2026-11-16 as the operator endpoint `POST /messaging/errors/{endpoint}/replay?limit=` (`Modulus.Messaging.Management`, `MapModulusMessagingErrors`), behind the Critical permission `messaging:errors:replay`; the limit is 1 to 1000 (default 100). Tests: `MessagingManagementApiTests` (8, net8 and net10: permission level, anonymous 401, no permission 403, replay with the endpoint and limit, default limit, bad limit 400).
- FR-09 CLI replay: not built, decided 2026-11-16. The `modulus` tool scaffolds code and never holds broker or database credentials, so a replay command would need operator secrets in the tool. The operator endpoint above is the route for the CLI half of the BRS acceptance: an operator calls it with a token, so the tool does not need the secrets.
- NFR-14 baseline shipped: the new public surface is in the `PublicAPI.Unshipped.txt` files, which the analyzers enforce. Moving it to `Shipped` is a release step.
- NFR-20 docs site: the Markdown docs in `docs/messaging/` exist; a published site does not.
- NFR-21 signed packages: needs the owner's code-signing certificate (F-25).
- Measured NFR targets (NFR-03 to NFR-07): not measured, need target hardware.
- FR-20 transaction filter: consumes already run inside the unit of work (`IConsumeUnitOfWork`), which commits or rolls back each attempt. It is not exposed as a filter.
- Filters and observers do not run for an outbox send until the dispatcher hands the row to the transport. The outbox stores the message first, so a send filter that refuses a message does not stop an outbox write.

**Notes on the new behaviour**
- The default body size limit (1 MiB) makes each send serialise the message once more to measure it. NFR-06 (overhead) was not measured after this change.
- A batch runs its filters once, with the first message of the batch.
- `UseEndpointNameFormatter` applies only to names the bus derives; call it before the consumers are added.

### Progress

**M0 (done)**

- [x] Core project (`src/messaging/Modulus.Messaging`): consumers, publish, send, per-message DI scope, `_error` and `_skipped` routing, `AddModulusMessaging`
- [x] InMemory transport (`src/messaging/Modulus.Messaging.Transports.InMemory`): per-endpoint pump, `_error` and `_skipped` queues
- [x] Test harness (`src/messaging/Modulus.Messaging.Testing`): `MessagingTestHarness`, `WaitUntilAsync`
- [x] M0 conformance kit: `TransportConformance.RunAsync(factory)` in `Modulus.Messaging.Testing` runs six transport checks (each once, one endpoint in send order, redeliver after delay, moved to error and to skipped are not redelivered, stop completes), each on its own endpoint. Run against InMemory (unit, the G0 gate), PostgreSQL and RabbitMQ (integration): all pass on net8.0 and net10.0 where the project runs
- [ ] M0 generator for consumer shapes: not started

**M1 slice 1 (done)**

- [x] Retry policies: Immediate, Interval, Intervals, Incremental, Exponential, with `Ignore<TException>()` (FR-07)
- [x] Delayed redelivery with `mod-redelivery-count` header (FR-08)
- [x] `Fault<TMessage>` published when a message is parked in `_error`; `IFault` stops fault-of-fault loops (FR-05)
- [x] Consume timeout, counted as a failure (FR-16)
- [x] Graceful stop: in-flight messages finish; after the stop timeout (30 s default, NFR-18) they go back to their queue
- [x] Settings at bus defaults and per consumer; the narrowest scope wins (FR-C1)
- [x] 17 unit tests, passing on net8.0 and net10.0, named with BRS IDs

**M1 item 1: transactional outbox on EF Core (done)**

- [x] `Modulus.Messaging.Outbox.EntityFrameworkCore`: publish and send from a scope write rows into the caller's `DbContext`; a dispatcher sends them only after the transaction commits (FR-10, RULE-01)
- [x] Dispatch claims rows under a lease, deletes each row after its send, and parks rows whose type cannot be rebuilt instead of dropping them (RULE-03)
- [x] 3 tests (SQLite): rolled-back transaction sends nothing, committed transaction sends once and clears the row, unresolvable type is parked
- [x] Timestamps are UTC `DateTime` so SQLite and the other providers translate the dispatch queries
- [x] Limit: RULE-09 (request/response is not captured by the outbox) is not implemented yet, because request/response (FR-06) is not built

**M1 item 2: idempotent inbox on EF Core (done)**

- [x] `Modulus.Messaging.Inbox.EntityFrameworkCore`: each consume attempt runs in one DbContext transaction with its inbox record; commit saves the consumer's writes, the inbox record and the outbox rows together, rollback drops all of them (RULE-06)
- [x] A message a consumer already handled is skipped and counted on `messaging.inbox.duplicates` (FR-11, RULE-04)
- [x] `InboxMaintenance.PurgeExpiredAsync` removes records outside the detection window (default 7 days in the BRS)
- [x] 3 tests (SQLite): duplicate applied once and counted, failed attempt rolls back its writes and inbox record, purge keeps recent records
- [x] Limit: a consumer's transaction joins an already-open DbContext transaction without committing it. A retrying provider (SQL Server, PostgreSQL with retry-on-failure) needs an execution strategy around the attempt; not added yet.

**M1 item 3: flow control (partly done)**

- [x] Rate limit per endpoint: at most N deliveries per period, sliding window (FR-15); own implementation, no new package
- [x] Kill switch: when the failure ratio over the last N outcomes reaches the threshold, the endpoint pauses for the restart timeout, then restarts with an empty window (FR-15)
- [x] 2 tests: deliveries spaced by the rate limit; the kill switch pauses after four failures and restarts after the timeout
- [x] Concurrency limit (`ConsumerOptions.ConcurrentMessageLimit`, default 1) through `IConcurrentDeliveryTransport`, which the bus calls before start. Implemented in all three transports (in-memory, RabbitMQ, PostgreSQL). Transports without the interface refuse a limit above 1 at start (`ConcurrencyTests`). Tests: in-memory unit 3, PostgreSQL integration 2 (ordered under 16 across two hosts, limit reached on one host), RabbitMQ integration 2
- [x] Circuit breaker (`CircuitBreakerOptions`, `EndpointControl`; 3 unit tests). Built on 2026-11-15 after all; D11 above is superseded. The kill switch remains the BRS criterion.

**M1 remaining**

- [ ] Ack after commit (RULE-02): done for the RabbitMQ transport (M2) and for the EF outbox/inbox transaction. The InMemory transport has no ack
- [x] Chaos harness and the G1 run (NFR-01): passed on PostgreSQL on 2026-11-15 and again on 2026-11-16 (1,000,000 messages, 102 kills, every message applied once). The InMemory transport cannot pass it on its own, since it loses messages on exit.

**M2 RabbitMQ transport (in progress)**

- [x] `Modulus.Messaging.Transports.RabbitMQ` (RabbitMQ.Client 7.2.2): durable quorum queue per endpoint, `_error` and `_skipped` queues, publisher confirms on publish
- [x] Acknowledged only after the bus has finished with the message (RULE-02); a stop waits for in-flight messages up to `StopTimeout` (NFR-18), then the broker takes them back
- [x] Delayed redelivery through a TTL queue that dead-letters back to the endpoint (BRS R-09 fallback, no delayed-message plugin needed)
- [x] Connection and topology recovery (NFR-02): the transport's own reconnect loop with a 1–60 s back-off (built 2026-11-16; the client's fixed-interval recovery is off)
- [x] TLS option and credentials from options (NFR-09); the message body is JSON, metadata in `mod-*` headers
- [x] `IMessageTransport.Start` now receives the consumer endpoints, so a transport can declare their queues
- [x] 4 integration tests on a real RabbitMQ 4 broker (Testcontainers on Podman): publish and send round trip, failing message redelivered then parked in `_error` with its fault headers, unhandled message parked in `_skipped`, in-flight message delivered again to the next host after a stop
- [x] Broker outage and recovery (NFR-02, passed on Podman on 2026-11-16 with the new reconnect loop; `NFR_02_the_bus_recovers_when_the_broker_restarts_without_a_host_restart`; passed on Podman, fails on Docker Desktop; the cause is not confirmed: a forwarder on a fixed port, which follows the container, did not fix it, and a restarted container did not answer on its new port within 60 s): the broker is stopped, a send during the outage fails (never reported as sent), the broker starts again, and the bus recovers and delivers without a host restart; 5 integration tests pass on net8.0 and net10.0
- [x] Throughput (G2, `G2_measures_end_to_end_throughput_with_publisher_confirms`): 50,000 messages sent through the bus with publisher confirms and consumed by one host take about 1.6 s, about 31–32k msg/s on the development machine (two runs), above the 25k target. Publishes do not hold the channel lock while waiting for confirms (a lock across the confirm wait had capped publishing at about 430 msg/s; the broker itself handles about 60k msg/s with concurrent confirms in a raw probe)
- [x] Bus overhead (NFR-06, measured 2026-11-16): `MessagingBenchmarks.SendAndHandle` (Send to the consumer running, in-memory transport, Release, BenchmarkDotNet) gives a p50 of 4.65 µs (mean 4.2 to 4.7 µs over two runs), under the 5 µs target. This is the bus's own cost with no broker round trip. Still open: the 10% regression gate in CI, and the same measurement on the target hardware.
- [ ] G2 on the target hardware. On this machine (Ryzen 5 8600G, 12 threads, Podman port proxy) the RabbitMQ run measured 23.4k and 23.3k msg/s on 2026-11-16 and 31–32k msg/s earlier, so it does not reach the 25k target reliably here. It is not judged until it runs on the target hardware.
- [x] TLS tested (NFR-09): RabbitMQ over AMQP TLS on its own container (5671), with the conformance suite passing when the certificate is trusted and a certificate nobody trusts refused. PostgreSQL with SSL on, verified as an encrypted session (`pg_stat_ssl`), the conformance suite passing with `SSL Mode=VerifyFull`, and an untrusted certificate refused. Both refusals must fail on a certificate error. Test certificates are created per fixture, never committed. The RabbitMQ transport gained `TlsCertificateValidation` (default: normal checks) to trust a private CA or pin one certificate
- [x] Publisher-confirm failure (a nacked publish, not an outage): `MaxQueueLength` (default unbounded) bounds the queues the transport declares with reject-publish; a publish the broker refuses throws `PublishException` and is never reported as sent (`RabbitMqPublishConfirmTests`). Queues that already exist keep their arguments, so set it before they are created
- [ ] Limit: a stop with the connection already down cannot requeue in-flight messages from this process; the broker requeues them when the connection drops, so no message is lost, but the handler's attempt is repeated

**M3 PostgreSQL transport (in progress)**

- [x] `Modulus.Messaging.Transports.PostgreSql` (Npgsql 10.0.3 / 8.0.9): one queue table (`messaging_queue`) with a `queue` column per endpoint; the table and index are created on start (`CreateSchema`, turn off when a migration owns the schema)
- [x] Competing hosts claim batches with `FOR UPDATE SKIP LOCKED` under a lease, so a row is handled by one host at a time (`RULE_02_competing_hosts_handle_each_message_exactly_once`: 300 messages over two hosts, each once)
- [x] A row is deleted only after the bus has finished with it (RULE-02); a stop releases unfinished rows at the stop timeout (NFR-18); a host that dies keeps its lease until it expires, then another host receives the row (at-least-once)
- [x] Delayed redelivery is a row with a visibility time in the future (FR-08); `_error` (with `mod-error-message`) and `_skipped` are queues in the same table (FR-09)
- [x] Database outage: sends fail with an error and polling resumes after the database restarts (NFR-02; passed on Podman; passed on Podman, fails on Docker Desktop for the same port reason; the test now has its own class, so it no longer breaks other tests)
- [x] 7 integration tests on a real PostgreSQL 17 container (Testcontainers on Podman), net10.0; net8.0 run recorded below
**M3 scheduler with cancel (FR-12, in progress)**

- [x] `IMessageScheduler`: `ScheduleSend(address, deliverAt, message)`, `SchedulePublish(deliverAt, message)`, `CancelScheduled(token)`; each schedule returns a token. A publish schedules one envelope per consumer endpoint under the same token
- [x] `IScheduledMessageTransport` is an optional transport capability; a transport without it refuses scheduling with `NotSupportedException`
- [x] In-memory transport: one timer per scheduled envelope; a cancel and the due time race under one lock, so a cancelled message never arrives and a delivered one cannot be cancelled
- [x] PostgreSQL transport: a scheduled row is visible at its time (`schedule_id` column, indexed); cancel deletes the unclaimed rows of the token, so a claimed row (already with a host) cannot be cancelled
- [x] Tests: 5 unit tests (arrives at its time, cancelled never arrives, publish reaches every consumer when due, cancel after delivery reports nothing, refusal on a transport without scheduling); 2 PostgreSQL integration tests (arrives at its time, cancelled never arrives)
- [x] RabbitMQ scheduling (R-09): TTL fallback, no delayed-message plugin. A scheduled message waits in a power-of-two TTL bucket queue (`mod-schedule.{ms}`) that dead-letters into `mod-schedule.due`; the due consumer forwards it to its endpoint once its time has come, or into the next bucket for the rest of the delay. Tests: arrives at its time through several buckets, a past time is delivered at once, cancel is refused (3 integration tests). The delayed-message plugin is not used
- [ ] Decision (2026-11-15, recorded): RabbitMQ scheduled-message cancel stays unsupported; the outbox path cancels by token instead. RabbitMQ scheduling: cancelling a scheduled message is not supported yet (it waits in a bucket that cannot be searched by token; `CancelScheduled` throws `NotSupportedException`). A failed schedule still rolls back best effort and reports its own error (`ScheduleRollbackTests`). A multi-envelope publish that fails partway can leave its first envelopes scheduled
- [x] Scheduling through the transactional outbox: inside a scope with an outbox, `IMessageScheduler` (now scoped, as `IBus` is) stores the message with its due time (`OutboxRecord.DeliverAt`), so it is sent only after the transaction commits and once it is due; the dispatcher skips rows that are not due (3 tests, `ScheduledOutboxTests`). Cancelling by token deletes the unclaimed rows of that schedule (`CancelScheduled`; the test covers a second cancel returning false). **Upgrade note:** the `messaging_outbox` table gets nullable `DeliverAt` and `ScheduleToken` columns; apps that use the EF outbox need a migration for them
- [x] Scheduling by delay: `ScheduleSend` and `SchedulePublish` take a `TimeSpan` measured from the call; a negative delay is refused (`ScheduleTests`, 2 new unit tests)
- [ ] Limit: on PostgreSQL the due time is the caller's timestamp, compared with the database clock; a skew between the app and database clocks moves delivery by the skew

**M3 time-to-live and priority (FR-13, in progress)**

- [x] Time-to-live: `ConsumerOptions.TimeToLive` (consumer or bus default) and `[MessageTimeToLive(ms)]` on the message type, which overrides the consumer option. The bus stamps `mod-expires-at` at send; at delivery an expired message goes to `_skipped` with `mod-skip-reason: expired` and no consumer runs. Because the check is in the bus, it works on every transport
- [x] Priority: `ConsumerOptions.Priority` or `[MessagePriority(n)]` (attribute wins); the bus stamps `mod-priority`; PostgreSQL stores it in a `priority` column and claims the highest priority first (`FR_13_higher_priority_is_consumed_first_on_postgresql`)
- [x] Tests: 3 unit (expired goes to `_skipped` unprocessed, within TTL is consumed, type attribute overrides the consumer); 2 PostgreSQL integration (expired goes to `_skipped`, higher priority first). PostgreSQL 11 of 11 on net10.0
- [x] RabbitMQ priority checked against a RabbitMQ 4 broker: quorum queues refuse `x-max-priority` (406 PRECONDITION_FAILED); classic queues accept it. Decision (owner, 2026-11-15): keep quorum queues, so RabbitMQ does not give priority. This is a documented limit (FR-13 says "higher priority is consumed first on RabbitMQ"; the BRS needs an owner decision to change it: an opt-in classic priority queue, or classic for all endpoints)
- [x] Priority on the in-memory transport: a per-endpoint priority queue (highest first, FIFO within a priority), replacing the FIFO channel (`FR_13_on_the_in_memory_transport_the_highest_priority_is_consumed_first`)

**M3 sagas, state machine (FR-17, in progress)**

- [x] `SagaStateMachine<TSaga>` in `Modulus.Messaging.Sagas`: `State`, `Event<T>().CorrelateBy(...)`, `Initially`, `During`, `When(...).Then(...).TransitionTo(...).Complete()`; `Publish`, `Send` and `Schedule` in the handler context run after the state is saved
- [x] Events are correlated to one instance; an event that does not start a saga throws `SagaNotStartedException` and is redelivered (UC-05); an event the state does not handle throws `UnexpectedSagaEventException` and changes nothing; a machine with an event without `CorrelateBy` is refused at start
- [x] `AddSaga<TMachine,TSaga>(endpoint)` registers one consumer per event type on one endpoint; `AddInMemorySagaRepository<TSaga>()` stores copies as JSON
- [x] Tests (`OrderSagaTests`, 6 unit): happy path, timeout path from a schedule, cancel path (the late timeout publishes nothing), early event redelivered, unhandled event, uncorrelated event refused
- [x] Completing a saga cancels its pending timeouts (2026-11-15). Each `Schedule` token is stored in `SagaInstance.PendingTimeouts`; schedules run before the save, a failed save cancels them, and a completed saga cancels all of its tokens after the instance is removed (`FR_17_completing_cancels_the_pending_timeout_so_it_never_reaches_the_saga`). A cancel that the transport refuses (RabbitMQ) is best effort: the timeout still arrives and is parked, as before
- [x] `Ignore()` in the reaction API (2026-11-15): the event is accepted and changes nothing; it cannot start a saga or combine with handlers, a transition or `Complete()` (`FR_17_an_ignored_event_changes_nothing_and_the_saga_still_completes`)
- [x] `If`, `IfElse` and `Catch` in the reaction API (2026-11-15, D5): a false `If` skips its block and accepts the event; a caught throw discards the attempt's state changes and effects and runs the compensation on the original saga; an uncaught throw changes nothing and is redelivered or parked (`SagaBranchingTests`, 5 tests). `Request` still needs request/response (FR-06), which is not built; `Finalize` is named `Complete()`
- [x] Concurrency control in the in-memory repository (FR-19, optimistic): `SagaInstance.Version`; a save or delete of a copy whose version is no longer stored throws `SagaConcurrencyException`, and the bus redelivers the message, which re-reads and applies its change. Tests: a stale save is refused; 50 increments with 8 workers, each forced to overlap, all apply (without the version check the same test ends at 7, so it proves the fix)
- [x] EF Core saga repository (FR-19) in `Modulus.Messaging.Sagas.EntityFrameworkCore`: `SagaRecord` table (`messaging_sagas`, keyed by saga type and correlation id), `AddEntityFrameworkSagaRepository<TSaga,TContext>()`. A save is an insert for a new instance (a racing insert is a conflict) or one `UPDATE ... WHERE Version = read version`; a delete checks the version the same way. Tests on SQLite: a stale save is refused; 50 increments with 8 workers on the database all apply (without the check the same test ends at 7). Found and fixed on the way: the version must be read from the column, not the JSON
- [x] Npgsql JSONB saga repository (FR-19) in `Modulus.Messaging.Sagas.PostgreSql`: `PostgreSqlSagaRepository<TSaga>` with `SchemaScript`; a save is an `INSERT ... ON CONFLICT DO NOTHING` or one `UPDATE ... WHERE version = read version`, a delete checks the version the same way. Tests on a PostgreSQL 17 container: a stale save is refused; 50 increments with 8 workers all apply. With the UPDATE check removed the same test fails (it ends at 0, not at 7 as the in-memory and EF runs do; the cause of the different count is not yet explained)
- [x] FR-17 `Request` (2026-11-16): no dedicated API. A saga sends the request, waits in a state and completes on the correlated reply (`SagaRequestReplyTests`, net8.0 and net10.0). A `Request` method would only wrap `Send`, `TransitionTo` and `CorrelateBy`.

**M3 consumer sagas (FR-18, done)**

- [x] A saga class implements `IInitiatedBy<TSaga,TMessage>`, `IOrchestrates<TSaga,TMessage>` or `IInitiatedByOrOrchestrates<TSaga,TMessage>` per message, and `ICorrelatedBy<TMessage>` (a static key) for each; `AddConsumerSaga<TSaga>(endpoint)` registers them. A missing `ICorrelatedBy` is refused at registration
- [x] The initiating message creates the instance; a later message moves it on; `Complete()` removes it; a message that arrives before its instance is redelivered; a second initiating message for an existing instance is refused and changes nothing
- [x] Tests (`ConsumerSagaTests`, 5 unit): creation and completion, early message redelivered, second initiation refused, timeout that orchestrates the saga, uncorrelated saga refused. 11 saga tests pass on net8.0 and net10.0

**M3 ordered processing (FR-14, in progress)**

- [x] `[PartitionKey]` on a message property (or `ConsumerOptions.Ordered` with the endpoint name as one partition): the bus stamps `mod-partition-key` on each message to an ordered consumer
- [x] `ConsumerOptions.Ordered`: priority does not apply to an ordered consumer, because a higher priority would let a later message of the same partition overtake an earlier one
- [x] PostgreSQL: a partitioned row is claimable only when no earlier row of its partition exists, so at most one row per partition is in flight across all hosts (`FR_14_messages_of_one_partition_are_processed_in_send_order_across_two_hosts`: 400 messages in 4 partitions over two hosts, each partition in send order)
- [x] A retried message keeps its sequence, so the messages sent after it wait for it (`FR_14_a_retried_message_keeps_its_place_in_its_partition`)
- [x] Scheduled messages have no partition: their send time is not their order, so they do not hold back other messages
- [x] Unit tests (in-memory): the key is stamped, an ordered consumer without a key uses its endpoint, priority is ignored for an ordered consumer, and 300 interleaved messages in 3 partitions stay in order. 30 of 30 core tests pass on net10.0 and net8.0
- [x] "Under concurrency 16" tested on in-memory (unit), PostgreSQL (2 hosts x 16, 500 messages in 5 partitions) and RabbitMQ (1 host x 16: 400 messages in 4 partitions kept in order; 200 unordered messages reach more than one in flight)
- [x] Across hosts on RabbitMQ (2026-11-15, plan step 10): `ConsumerOptions.SingleActiveConsumer` sets `x-single-active-consumer` on the endpoint's quorum queue; one host at a time receives the endpoint, so its order holds across hosts (`FR_14_a_single_active_endpoint_hands_its_messages_to_one_host_at_a_time_across_two_hosts`, and a mutation check fails it with the option off). Without the option, competing consumers do not enforce partition order. A queue that already exists keeps its arguments, so it must be recreated to change. In-memory and PostgreSQL refuse the option (PostgreSQL already orders per partition across hosts)

- [x] Retention: a delivered row is deleted on success, so there is nothing to keep. Parked rows (`_error`, `_skipped`) are purged by `PurgeParkedAsync(olderThan)` (unclaimed rows only; 2 integration tests). Nothing purges on a timer: a maintenance job must call it
- [x] Migration script: `docs/messaging/postgresql-queue-schema.sql` (default schema and table); `PostgreSqlTransport.SchemaScript(options)` generates it for others. `SchemaScriptTests` fails if the file and the code differ
- [x] Purge timer: `ParkedRetention` (null keeps parked rows) with `PurgeInterval` (default 1 hour) runs the purge while the transport runs; a failure goes to `PurgeFailed` and is retried at the next interval (2 integration tests). The PostgreSQL integration assembly runs its tests one at a time, because a purge removes every parked row and other tests wait for parked rows
- [x] Migration script: `docs/messaging/postgresql-schema.sql` (idempotent; it includes the claim index and the tenant column). `PostgreSqlSchemaScriptTests` fails when it drifts from the transport's own script.
- [ ] Not built yet: an index tuned for the 25k msg/s target (not measured on target hardware), and the PostgreSQL throughput measurement
- [ ] Limit: the readiness wait for the test container uses the TCP port, because rootless Podman cannot exec the default `pg_isready` check here; the test fixture also waits for a real query and clears pooled connections after a database restart

**M4 tracing (FR-24, partly done, 2026-11-15)**

- [x] `ActivitySource` "Modulus.Messaging": a producer span for each publish (`publish {Type}`) and send (`send {endpoint}`), and a consumer span for each consume attempt (`process {endpoint}`), parented from the producer through the `mod-traceparent` header (`MessagingHeaders.TraceParent`). The header travels with the envelope, so it survives the outbox and the queue
- [x] A consume that throws sets its span's status to Error
- [x] Tests (`TracingTests`, net8.0 and net10.0): the consumer span shares the sender's trace and is its child; a failing consume is marked as an error
- [x] `messaging.system` names the transport (2026-11-15, D9): `rabbitmq`, `postgresql`, `in-memory`, via `IMessageTransport.SystemName` (default `modulus`). The outbox trace test (`FR_24_an_outbox_message_keeps_its_trace_until_the_dispatcher_sends_it`) shows the consumer span joins the publishing span across the outbox. The dispatcher creates no span of its own: the trace context is stamped when the message is published, and the outbox row carries it. Outbox publish and send now start a producer span, as the direct path does

**M4 health checks (FR-24, partly done, 2026-11-15)**

- [x] `IMessageTransportHealth` (optional transport capability) and `MessagingHealthCheck`, registered with `AddModulusMessagingHealthCheck()`. A transport without the capability reports healthy, and the description says so
- [x] RabbitMQ: available only when the connection is open and the broker answers a passive declare of `amq.direct` within 5 s. The connection flag alone stays true after the broker goes away, so the round trip is what makes the check work
- [x] PostgreSQL: available when `SELECT 1` succeeds
- [x] Tests: 5 unit (`HealthCheckTests`); integration on Podman: the broker and database each go healthy, unhealthy while down, and healthy again after restart (`RabbitMqOutageTests`, `PostgreSqlOutageTests`)
- [ ] Not yet: in-memory has no broker, so it always reports healthy

**M3 tenant context (FR-21, bus level, 2026-11-15)**

- [x] `MessageTenant.Enter(id)` (AsyncLocal scope, restores the previous tenant on dispose). Messages sent in a scope carry the tenant in `mod-tenant-id` (`MessagingHeaders.TenantId`); the header travels through every transport and the outbox, since they carry all headers
- [x] A consumer runs with the tenant of its message (`MessageTenant.Current` and `ConsumeContext.TenantId`), so one message never runs in another tenant's scope
- [x] `ConsumerOptions.RequireTenant`: a message without a tenant is moved to `_skipped` with reason `no-tenant` before any consumer or rate-limit slot runs (BRS acceptance: "a tenant-scoped consumer rejects a message with no tenant")
- [x] Tests (`TenantTests`, 5 unit, net8.0 and net10.0): the sender's tenant reaches the consumer; two messages each keep their own tenant; a tenant-scoped consumer parks a tenant-less message and runs nothing; a tenant-scoped consumer handles a tenant message; the scope restores on dispose
- [x] Bridge to the framework's tenant (2026-11-15): `Modulus.Messaging.Modulus` (new project, `AddModulusTenantMessaging()`). Two hooks in the core make it possible: `IMessageAmbientContext` (wraps the scoped bus and scheduler, so a message from a request carries that request's company; a host-context scope clears any outer one) and `IConsumeScope` (verifies the header's company through `VerifyTenantAsync` and enters it in the bus's consume frame; an unknown, inactive or malformed company refuses the message)
- [x] Tests: 5 core hook tests (`AmbientHookTests`), 5 adapter tests (`AdapterTests`), both TFMs
- [x] Row-level isolation (FR-21, built 2026-11-15, commit 4aba7b86): RLS on the PostgreSQL queue table (`TenantRowLevelSecurity`, opt-in, 32 integration tests with it), `TenantId` on the inbox record, and the tenant header on outbox rows. The remaining note: the transport is host-level by design (D7).

**M4 metrics (FR-24, partly done, 2026-11-15)**

- [x] Meter `Modulus.Messaging`: `messaging.client.sent.messages` (publish and send), `messaging.client.consumed.messages` (attempts that ran the consumer and committed), `messaging.process.duration` (seconds per attempt, with `error.type` on failure), `messaging.parked.messages` (tag `reason`: `error`, `expired`, `no-tenant`, `unhandled`). Each carries `messaging.destination.name`
- [x] Tests (`MetricsTests`, net8.0 and net10.0): counts and timings for successful attempts; a failed attempt is timed with its error type and counted as parked, and not as consumed
- [x] Queue-depth gauge `modulus.messaging.queue.depth{messaging.destination.name}` for the PostgreSQL transport (2026-11-15, step 5): sampled every `QueueDepthInterval` (default 15 s, null turns it off), the gauge reports the last sample so a scrape never waits on the database (`D9_the_queue_depth_gauge_reports_the_rows_waiting_in_each_queue`)

**M4 structured logs (FR-24, 2026-11-15)**

- [x] The bus logs under category `Modulus.Messaging` (via `ILoggerFactory` when the host has one, else silent): a message parked in `_error` (Error, with the exception and the attempt count), a message parked in `_skipped` for no consumer, for no tenant, or as expired (Warning), a redelivery scheduled (Warning, with the delay and redelivery number), and an inbox duplicate (Information). Each has the message id and the endpoint as structured properties
- [x] Tests (`LoggingTests`, 3 unit): the error log carries the endpoint, the attempt count and the exception; an unhandled message and a message without a required tenant each log their reason
- [x] A kill-switch trip is logged as a Warning with the endpoint and the pause length (`LoggingTests`, 4 unit)
- [x] Log scope per delivery (2026-11-15, D9): every line of a delivery carries `TraceId`, `SpanId`, `MessageId`, `Endpoint` and `TenantId`, the trace ids read from `mod-traceparent` so they are present without a listener (`FR_24_every_line_of_a_delivery_carries_its_trace_message_endpoint_and_company`)

**M3 tenant-scoped saga keys (FR-21, 2026-11-15)**

- [x] `SagaKeys.ForTenant(tenant, key)`: outside a tenant the key is unchanged, so existing sagas are unaffected; inside one it is a Guid derived from the tenant and the correlation key. Both saga consumers (state machine and consumer sagas) use it, so two tenants with the same correlation id get two instances. The saga's `CorrelationId` holds the derived key
- [x] Test (`FR_21_the_same_correlation_key_in_two_tenants_is_two_saga_instances`): fails without the scoping, passes with it
- [ ] Note: the derived key uses the tenant string as the header carries it; producers must send one consistent form per tenant (the adapter sends the id in `N` form)

**M3 company isolation, end to end (FR-21, 2026-11-15)**

- [x] `CompanyIsolationTests`: two companies each with one product in a shared module database; a message per company; each consumer counts 1, not 2. The consumer's company comes from the verified message (`ModulusTenantConsumeScope`), and the module's EF tenant filter does the rest
- [x] Mutation check: without the consume scope the consumer fails closed (count 0). The test fake is fail-closed like the framework's accessor when nothing is resolved
- [ ] Hazard, not a bug in the framework: a consumer whose company is not entered runs in whatever `ICurrentTenant` reports. With multi-tenancy on, an unresolved company fails closed; the explicit host context sees every company. An app must register `AddModulusTenantMessaging()` for company consumers; the test shows that without it, a host-context accessor reads both companies (count 2)
- [x] Inbox tenant column (2026-11-15, D7): `InboxRecord.TenantId`, written from `MessageTenant.Current` and indexed. **Upgrade note:** existing deployments add the column with a migration. RLS on the PostgreSQL queue table is not built (D7): the transport is host-level by design and isolation is enforced at consume time, so RLS would add no protection without a bypass role

**Integration verification after the 2026-11-15 changes (Podman, net10.0)**

- [x] `Modulus.Messaging.Transports.PostgreSql.Integration.Tests`: 23 of 23
- [x] `Modulus.Messaging.Transports.RabbitMQ.Integration.Tests`: 16 of 16 (one run failed on a race in the test's poll helper: a basic-get on a queue not yet declared closes the channel, and the helper's `finally` then threw `AlreadyClosedException`. The helper now closes only an open channel; the test passes on repeated runs)
- [x] `Modulus.Messaging.Sagas.PostgreSql.Integration.Tests`: 2 of 2
- [x] `Modulus.Messaging.Integration.Tests`: 6 of 6
- [x] In-memory conformance: `TransportConformanceTests` in `tests/unit/Modulus.Messaging.Tests` runs the shared suite on the in-memory transport, so no separate integration project is needed. The Docker Desktop restart-test caveat above still applies to the broker transports.

**Progress, fifth pass (2026-11-15)**

- [x] Outbox and inbox on Npgsql (`Modulus.Messaging.Outbox.Npgsql`, `Modulus.Messaging.Inbox.Npgsql`): a unit of work shared by the app's writes, the outbox rows and the inbox record; the dispatcher claims with `FOR UPDATE SKIP LOCKED`. 7 of 7 integration tests on PostgreSQL 17.
- [x] Request/response, FR-06 (`IRequestClient.GetResponse`, `ConsumeContext.RespondAsync`, `AddRequestClient<TResponse>()`): replies go to a per-process reply endpoint; a timeout raises `RequestTimeoutException`; an encrypted response is sealed. Saga request steps: no dedicated `Request` API. A saga sends the request with `Send`, moves to a waiting state, and completes on the reply, which is correlated by its key (`SagaRequestReplyTests`, net8.0 and net10.0). A dedicated API would only wrap these three calls.
- [x] Circuit breaker (optional, FR-15): `ConsumerOptions.CircuitBreaker`; opens after consecutive failures, admits one trial message after `OpenDuration`, closes on success, opens again on failure. 3 unit tests.
- [x] Public API baseline frozen: the unshipped entries of the messaging packages moved into `PublicAPI.Shipped.txt`; a change is a new unshipped entry.
- [x] Migration guide from MassTransit v8: `docs/messaging/migration-from-masstransit.md` (NFR-19).
- [x] Claim index on the PostgreSQL queue table: `(queue, priority DESC, id)`. The claim sorted the whole backlog on every poll (a sequential scan and a sort of a million rows, 427 ms per claim); with the index it is 0.5 ms. Existing deployments add the index with the migration script. Found by the G1 chaos run.
- [x] G1 chaos run (NFR-01, 2026-11-15): 1,000,000 messages through PostgreSQL with 105 worker processes killed at random and restarted. Result: every message applied once (ledger count 1,000,000), nothing parked, every message recorded in the inbox, 16 min 53 s. Harness in `tests/integration/Modulus.Messaging.Chaos.Tests` (`--filter Category=Chaos`). The first run stalled at about 130 messages a second (the claim sorted the whole backlog); the claim index fixed it. One machine, one run: not the 1M-under-chaos evidence the owner may want for a release, which is a repeat on target hardware.
- [x] (G1 harness committed, see above.)
- [x] Row-level isolation on the PostgreSQL queue table (FR-21, 2026-11-15): the queue table has a `tenant_id` column; `TenantRowLevelSecurity` (off by default) turns on a policy so a company's session sees its own rows, a host sees the untagged rows, a session with no company sees nothing, and members of `TransportRole` (the transport) see all. The owner applies the policy (`docs/messaging/postgresql-tenant-rls.sql`). Tested as non-superuser login roles: 32 of 32 PostgreSQL transport tests.
- [x] CLI scaffolding (FR-23, 2026-11-15): `modulus add-messaging [--transport postgresql|rabbitmq|inmemory]` adds the packages, the bus (with the outbox and inbox on PostgreSQL) and the settings to an API host; idempotent; 7 unit tests of the wiring. End-to-end check (2026-11-15): generated an API app from the packed `src/` projects (84 packages, local feed), ran `add-messaging`, the app built with 0 errors and 0 warnings, and it started against PostgreSQL 17 with its health endpoint at 200 and the outbox, queue and inbox tables created. The check found two defects, both fixed: the feature services file lacked the bus's namespaces, and the inbox table was not created on start. Round trip (2026-11-16): on the same kind of generated app, with a probe consumer added, a message sent inside `INpgsqlUnitOfWork` (transactional outbox) reached the consumer with the same id; PostgreSQL showed one `messaging_inbox` row and empty `messaging_queue` and `messaging_outbox` tables; the log had no failures. A send outside a transaction is refused with an error, as designed. The probe was scratch code, not in the CLI templates.
- [ ] Signed packages: needs the code-signing certificate the owner has to obtain (F-25).
- [ ] Owner-only and environment items: hardware measurements (NFR-06, G2), Docker Desktop restart tests, the TradeFlow sample (G3, `samples/` off-limits), G0 consumer-shape generator (samples), G4 production gate.

### Known limits

- In-memory messages are lost if the process exits (no durability).
- Delayed redeliveries live in a timer task, so a stop before the delay ends loses them (in-memory only).
- `Modulus.Messaging` depends on `Microsoft.Extensions.DependencyInjection.Abstractions` and `Hosting.Abstractions` (Microsoft packages, the repo convention). No third-party packages.
- Public API is tracked in `PublicAPI.Shipped.txt` / `Unshipped.txt`; package validation is off until the first release (same as `Modulus.Search`).
