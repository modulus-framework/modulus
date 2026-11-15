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

As of 2026-11-15: M0 done. M1 partly done (outbox, inbox, retries, redelivery, faults, timeout, drain, settings, rate limit, kill switch); its concurrency limit, circuit breaker and chaos run are open. M2 RabbitMQ transport built and integration-tested; G2 throughput measured at about 31k msg/s on the development machine, with latency overhead not yet measured. M3 in progress: PostgreSQL transport, scheduler with cancel, TTL, priority (not on RabbitMQ, see its section), and ordered processing by partition are built; sagas (FR-17 to FR-19), tenant context and the TradeFlow sample are not started.

### Still open

- **FR-14, "under concurrency 16":** needs the concurrency limit (`ConcurrentMessageLimit`) in M1, which needs a transport that delivers concurrently. Ordering is tested today only sequentially across hosts. The PostgreSQL per-partition claim is the rule that concurrent delivery must keep.
- **Sagas, M3:** state-machine sagas (FR-17), consumer sagas (FR-18), and saga repositories on EF Core, Npgsql JSONB and in-memory (FR-19). Not started.
- **Tenant context, M3:** tenant context on messages with RLS-aware scope (FR-21). Not started.
- **TradeFlow sample, M3 gate G3:** orders to billing on PostgreSQL. Not started.

### Progress

**M0 (done)**

- [x] Core project (`src/messaging/Modulus.Messaging`): consumers, publish, send, per-message DI scope, `_error` and `_skipped` routing, `AddModulusMessaging`
- [x] InMemory transport (`src/messaging/Modulus.Messaging.Transports.InMemory`): per-endpoint pump, `_error` and `_skipped` queues
- [x] Test harness (`src/messaging/Modulus.Messaging.Testing`): `MessagingTestHarness`, `WaitUntilAsync`
- [ ] M0 conformance kit (transport conformance suite): not started
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
- [ ] Concurrency limit (`ConcurrentMessageLimit`): needs a transport that delivers concurrently. The InMemory pump is sequential; the RabbitMQ transport is sequential per endpoint too (prefetch only buffers). Deferred until a transport delivers concurrently
- [ ] Circuit breaker as a separate mechanism: not built. The kill switch covers the BRS acceptance criterion; Polly's circuit breaker is already used in `Modulus.Sagas`

**M1 remaining**

- [ ] Ack after commit (RULE-02): done for the RabbitMQ transport (M2) and for the EF outbox/inbox transaction. The InMemory transport has no ack
- [ ] Chaos harness and the G1 run (NFR-01): needs the durable transports; the InMemory transport loses messages on exit, so it cannot pass G1 on its own

**M2 RabbitMQ transport (in progress)**

- [x] `Modulus.Messaging.Transports.RabbitMQ` (RabbitMQ.Client 7.2.2): durable quorum queue per endpoint, `_error` and `_skipped` queues, publisher confirms on publish
- [x] Acknowledged only after the bus has finished with the message (RULE-02); a stop waits for in-flight messages up to `StopTimeout` (NFR-18), then the broker takes them back
- [x] Delayed redelivery through a TTL queue that dead-letters back to the endpoint (BRS R-09 fallback, no delayed-message plugin needed)
- [x] Automatic connection and topology recovery (NFR-02); the recovery interval is the client's fixed default (5 s), not the 1–60 s back-off the BRS asks for
- [x] TLS option and credentials from options (NFR-09); the message body is JSON, metadata in `mod-*` headers
- [x] `IMessageTransport.Start` now receives the consumer endpoints, so a transport can declare their queues
- [x] 4 integration tests on a real RabbitMQ 4 broker (Testcontainers on Podman): publish and send round trip, failing message redelivered then parked in `_error` with its fault headers, unhandled message parked in `_skipped`, in-flight message delivered again to the next host after a stop
- [x] Broker outage and recovery (NFR-02, `NFR_02_the_bus_recovers_when_the_broker_restarts_without_a_host_restart`): the broker is stopped, a send during the outage fails (never reported as sent), the broker starts again, and the bus recovers and delivers without a host restart; 5 integration tests pass on net8.0 and net10.0
- [x] Throughput (G2, `G2_measures_end_to_end_throughput_with_publisher_confirms`): 50,000 messages sent through the bus with publisher confirms and consumed by one host take about 1.6 s, about 31–32k msg/s on the development machine (two runs), above the 25k target. Publishes do not hold the channel lock while waiting for confirms (a lock across the confirm wait had capped publishing at about 430 msg/s; the broker itself handles about 60k msg/s with concurrent confirms in a raw probe)
- [ ] Not yet measured: the 5 µs p50 overhead (NFR-06) and the 10% regression gate in CI; the throughput run is a single host on a laptop-class machine through a Podman port proxy, so G2 must be judged again on the target hardware
- [ ] Not tested yet: TLS, publisher-confirm failure
- [ ] Limit: a stop with the connection already down cannot requeue in-flight messages from this process; the broker requeues them when the connection drops, so no message is lost, but the handler's attempt is repeated

**M3 PostgreSQL transport (in progress)**

- [x] `Modulus.Messaging.Transports.PostgreSql` (Npgsql 10.0.3 / 8.0.9): one queue table (`messaging_queue`) with a `queue` column per endpoint; the table and index are created on start (`CreateSchema`, turn off when a migration owns the schema)
- [x] Competing hosts claim batches with `FOR UPDATE SKIP LOCKED` under a lease, so a row is handled by one host at a time (`RULE_02_competing_hosts_handle_each_message_exactly_once`: 300 messages over two hosts, each once)
- [x] A row is deleted only after the bus has finished with it (RULE-02); a stop releases unfinished rows at the stop timeout (NFR-18); a host that dies keeps its lease until it expires, then another host receives the row (at-least-once)
- [x] Delayed redelivery is a row with a visibility time in the future (FR-08); `_error` (with `mod-error-message`) and `_skipped` are queues in the same table (FR-09)
- [x] Database outage: sends fail with an error and polling resumes after the database restarts (NFR-02)
- [x] 7 integration tests on a real PostgreSQL 17 container (Testcontainers on Podman), net10.0; net8.0 run recorded below
**M3 scheduler with cancel (FR-12, in progress)**

- [x] `IMessageScheduler`: `ScheduleSend(address, deliverAt, message)`, `SchedulePublish(deliverAt, message)`, `CancelScheduled(token)`; each schedule returns a token. A publish schedules one envelope per consumer endpoint under the same token
- [x] `IScheduledMessageTransport` is an optional transport capability; a transport without it refuses scheduling with `NotSupportedException`
- [x] In-memory transport: one timer per scheduled envelope; a cancel and the due time race under one lock, so a cancelled message never arrives and a delivered one cannot be cancelled
- [x] PostgreSQL transport: a scheduled row is visible at its time (`schedule_id` column, indexed); cancel deletes the unclaimed rows of the token, so a claimed row (already with a host) cannot be cancelled
- [x] Tests: 5 unit tests (arrives at its time, cancelled never arrives, publish reaches every consumer when due, cancel after delivery reports nothing, refusal on a transport without scheduling); 2 PostgreSQL integration tests (arrives at its time, cancelled never arrives)
- [ ] Not yet: scheduling through the transactional outbox (a scheduled message is written to the transport directly, not captured by the outbox), RabbitMQ scheduling (delayed plugin or TTL fallback, R-09), scheduling by delay (`TimeSpan`)
- [ ] Limit: on PostgreSQL the due time is the caller's timestamp, compared with the database clock; a skew between the app and database clocks moves delivery by the skew

**M3 time-to-live and priority (FR-13, in progress)**

- [x] Time-to-live: `ConsumerOptions.TimeToLive` (consumer or bus default) and `[MessageTimeToLive(ms)]` on the message type, which overrides the consumer option. The bus stamps `mod-expires-at` at send; at delivery an expired message goes to `_skipped` with `mod-skip-reason: expired` and no consumer runs. Because the check is in the bus, it works on every transport
- [x] Priority: `ConsumerOptions.Priority` or `[MessagePriority(n)]` (attribute wins); the bus stamps `mod-priority`; PostgreSQL stores it in a `priority` column and claims the highest priority first (`FR_13_higher_priority_is_consumed_first_on_postgresql`)
- [x] Tests: 3 unit (expired goes to `_skipped` unprocessed, within TTL is consumed, type attribute overrides the consumer); 2 PostgreSQL integration (expired goes to `_skipped`, higher priority first). PostgreSQL 11 of 11 on net10.0
- [x] RabbitMQ priority checked against a RabbitMQ 4 broker: quorum queues refuse `x-max-priority` (406 PRECONDITION_FAILED); classic queues accept it. Decision (owner, 2026-11-15): keep quorum queues, so RabbitMQ does not give priority. This is a documented limit (FR-13 says "higher priority is consumed first on RabbitMQ"; the BRS needs an owner decision to change it: an opt-in classic priority queue, or classic for all endpoints)
- [ ] Not built: priority on the in-memory transport (FIFO channel)

**M3 ordered processing (FR-14, in progress)**

- [x] `[PartitionKey]` on a message property (or `ConsumerOptions.Ordered` with the endpoint name as one partition): the bus stamps `mod-partition-key` on each message to an ordered consumer
- [x] `ConsumerOptions.Ordered`: priority does not apply to an ordered consumer, because a higher priority would let a later message of the same partition overtake an earlier one
- [x] PostgreSQL: a partitioned row is claimable only when no earlier row of its partition exists, so at most one row per partition is in flight across all hosts (`FR_14_messages_of_one_partition_are_processed_in_send_order_across_two_hosts`: 400 messages in 4 partitions over two hosts, each partition in send order)
- [x] A retried message keeps its sequence, so the messages sent after it wait for it (`FR_14_a_retried_message_keeps_its_place_in_its_partition`)
- [x] Scheduled messages have no partition: their send time is not their order, so they do not hold back other messages
- [x] Unit tests (in-memory): the key is stamped, an ordered consumer without a key uses its endpoint, priority is ignored for an ordered consumer, and 300 interleaved messages in 3 partitions stay in order. 30 of 30 core tests pass on net10.0 and net8.0
- [ ] Not yet: "under concurrency 16" (the BRS acceptance criterion). Every current transport delivers one message at a time per endpoint, so the test is sequential per partition across hosts, not concurrent within a host. The concurrency limit needs a concurrent delivery mode, which is still open (see the M1 concurrency item). The per-partition rule in PostgreSQL is already the one that concurrency will need
- [ ] Not yet: the RabbitMQ and in-memory transports do not enforce partitions across concurrent consumers (RabbitMQ is sequential per endpoint, so its order holds today; the in-memory pump is one loop)

- [ ] Not built yet: retention/purge of delivered rows, a per-endpoint delivery-count limit (the count is recorded, not enforced), an index tuned for the 25k msg/s target (not measured), the migration script for the schema, and the PostgreSQL throughput measurement
- [ ] Limit: the readiness wait for the test container uses the TCP port, because rootless Podman cannot exec the default `pg_isready` check here; the test fixture also waits for a real query and clears pooled connections after a database restart

### Known limits

- In-memory messages are lost if the process exits (no durability).
- Delayed redeliveries live in a timer task, so a stop before the delay ends loses them (in-memory only).
- `Modulus.Messaging` depends on `Microsoft.Extensions.DependencyInjection.Abstractions` and `Hosting.Abstractions` (Microsoft packages, the repo convention). No third-party packages.
- Public API is tracked in `PublicAPI.Shipped.txt` / `Unshipped.txt`; package validation is off until the first release (same as `Modulus.Search`).
