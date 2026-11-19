# Cobytelabs.Messaging — Business Requirements Specification (BRS)

Nov 14, 2026 · @Azim Mahmud

## 1. Purpose and document control

This BRS states what the business needs from Cobytelabs.Messaging, an MIT-licensed message bus for .NET that replaces MassTransit across the product portfolio. It defines the objectives, scope, requirements and acceptance criteria that the [system design](https://claude.ai/artifact/LnRX2AeEnQWReoeamQXLTy) must satisfy; the design says how, this document says what and why.

| Item | Value |
| --- | --- |
| Document | Business Requirements Specification |
| Product | Cobytelabs.Messaging (working name) |
| Version | 1.0 draft |
| Status | For review |
| Owner | Product owner and architect |
| Related | System design, sections 1 to 19 |
| Audience | Product owner, architect, framework engineers, product teams, operations, QA |

### Requirement conventions

- IDs: `OBJ` business objective, `BR` business requirement, `FR` functional requirement, `NFR` non-functional requirement, `RULE` business rule, `UC` use case.
- Priority uses MoSCoW: **Must** (v1 cannot ship without it), **Should** (v1 unless it threatens the date), **Could** (v2), **Won't** (out of scope).
- FR numbers match the system design's FR-01 to FR-25, so the two documents trace one to one.

## 2. Business context and problem statement

Every product in the portfolio relies on MassTransit for asynchronous messaging, and MassTransit's move to a commercial licence turns a free dependency into a recurring cost and a support risk for each product.

### Current situation

- The portfolio's standard stack is .NET, PostgreSQL, Redis and MassTransit, applied through the Modulus framework to every ERP and SaaS product.
- MassTransit v9 is a commercial product that must be licensed ([NuGet listing](https://www.nuget.org/packages/MassTransit/9.1.1)).
- MassTransit v8 remains open source, but security patches were announced only through 2026 ([announcement](https://masstransit.io/introduction/v9-announcement)).
- The price announced in 2025 for small and medium businesses was 400 USD per month or 4,000 USD per year ([summary](https://milanjovanovic.tech/blog/mediatr-and-masstransit-going-commercial-what-this-means-for-you)); current pricing must be confirmed with the vendor.

### Problems to solve

| # | Problem | Business impact |
| --- | --- | --- |
| P1 | Staying on v8 means no security patches after the announced window | Compliance and security exposure in customer-facing products |
| P2 | Moving to v9 adds a recurring licence cost | Higher cost base for a bootstrapped portfolio; cost repeats as products and customers grow |
| P3 | The products use only a core slice of MassTransit | Paying for, and carrying, a large surface that is never used |
| P4 | A separate broker is required even for a single modular monolith | Extra infrastructure to host, secure and monitor for small deployments |
| P5 | Tenancy, outbox and tracing are wired by hand in each product | Repeated effort and inconsistent reliability between products |

### Opportunity

A small bus that the business owns removes the licence dependency, fits the Modulus conventions, and lets a monolith run durable messaging on the PostgreSQL database it already has.

## 3. Business objectives and success measures

The project succeeds when every portfolio product can run production messaging with no MassTransit licence, no loss of reliability, and less setup effort than today.

| ID | Objective | Measure | Target |
| --- | --- | --- | --- |
| OBJ-1 | Remove the commercial licence dependency | Products in production that reference MassTransit v9 | 0 |
| OBJ-2 | Leave unpatched MassTransit v8 | New products started on Cobytelabs.Messaging after 1.0; existing products migrated | 100% of new products; first existing product within 3 months of 1.0 |
| OBJ-3 | Keep or improve reliability | Lost or double-applied messages in chaos tests and in production | 0 |
| OBJ-4 | Make migration cheap | Effort to migrate one typical module (consumers, outbox, one saga) | 2 working days or less |
| OBJ-5 | Cut infrastructure for small deployments | Brokers required for a single modular monolith | 0 (PostgreSQL transport) |
| OBJ-6 | Standardise cross-cutting behaviour | Products that hand-wire tenancy, outbox or tracing for messaging | 0 on Modulus |
| OBJ-7 | Keep ownership affordable | Engineers needed to maintain the bus after 1.0 | 1 to 2, part time |
| OBJ-8 | Deliver on a known timeline | 1.0 release; 2.0 feature set | About 20 weeks from start; about 26 weeks after 1.0 |

Targets for OBJ-4 and OBJ-7 are planning assumptions to be confirmed during the pilot.

## 4. Scope

v1 delivers MassTransit's core feature set on three transports; v2 adds interoperability, cloud transports and operations tooling; advanced MassTransit features that the portfolio does not use stay out.

### In scope for v1

- Messaging patterns: publish, send, request/response, consumers, batch consumers, fault consumers.
- Reliability: retry, delayed redelivery, error and skipped queues, transactional outbox, idempotent inbox, in-memory outbox, transaction filter.
- Workflows: state machine sagas and consumer sagas, with saga requests, schedules and timeouts.
- Scheduling: delayed send and publish with cancellation.
- Flow control: concurrency limit, prefetch, partitioner, rate limiter, kill switch, circuit breaker, consume timeout.
- Transports: InMemory, RabbitMQ, PostgreSQL.
- Storage: EF Core and raw Npgsql for outbox, inbox and sagas; in-memory saga repository.
- Cross-cutting: multi-tenancy, payload encryption, OpenTelemetry, health checks, filters and observers.
- Developer experience: MassTransit-style configuration at every scope, source-generated registration, test harness, Modulus integration and CLI scaffolding, migration guide.

### In scope for v2

- Wire adapters for MassTransit and Rebus, for gradual migration.
- Azure Service Bus and Amazon SQS/SNS transports.
- Operations: error-queue dashboard, messaging CLI, AsyncAPI generation, saga diagram export.
- Depth: Redis saga repository, composite events, push-based outbox, priority on the PostgreSQL transport.

### Out of scope

| Item | Reason |
| --- | --- |
| In-process mediator | Covered by the separate mediator package |
| Courier (routing slips) | Complex, not used in the portfolio |
| Job consumers and job service | Complex, not used in the portfolio |
| Riders (Kafka, Event Hubs) | No product needs streaming transports |
| MessageData (claim check) | Body size limit applies instead |
| Quartz.NET and Hangfire schedulers | Built-in scheduler covers delays |
| Multiple buses per host | One bus per host is sufficient |
| Exactly-once delivery | Not achievable across broker and database; at-least-once plus inbox instead |
| SQL Server transport, MongoDB sagas, Newtonsoft serializer | Demand-driven backlog; added when a product needs one |

## 5. Stakeholders and user classes

Five groups have a stake in the bus; application developers are the primary users and their migration effort decides adoption.

| Stakeholder | Role | What they need | Involvement |
| --- | --- | --- | --- |
| Product owner / founder | Sponsor, approver | No licence cost, predictable delivery, low maintenance | Approves scope, gates and release |
| Architect | Design authority | Clean abstractions, MassTransit parity on core features | Owns design and ADRs |
| Framework engineers | Builders and maintainers | Small codebase, strong test suites | Build, review, release |
| Application developers | Primary users | Familiar API, minimal migration work, good errors and docs | Pilot migration, feedback |
| Operations / DevOps | Run it in production | Health checks, metrics, replay of failed messages, safe topology deployment | Define alerts and runbooks |
| QA | Verify behaviour | Deterministic test harness, virtual clock | Own acceptance tests |
| End customers (tenants) | Indirect | Their data stays isolated; no lost or duplicated business events | None directly |

### User classes

| Class | Typical tasks | Skill assumed |
| --- | --- | --- |
| Application developer | Write consumers and sagas, publish events, configure retry | C#, EF Core; MassTransit experience helpful |
| Module author (Modulus) | Define integration events in contracts, scaffold consumers with the CLI | Modulus conventions |
| Operator | Deploy topology, watch dashboards, replay errors, tune limits in appsettings | Broker and PostgreSQL operations |
| Test engineer | Write harness tests for consumers and sagas | xUnit |

## 6. Assumptions, constraints and dependencies

The plan depends on PostgreSQL being present in every product and on the scope staying inside what one or two engineers can maintain.

### Assumptions

- A-1: Every portfolio product already runs PostgreSQL and can host the messaging schema in the same database.
- A-2: Products use MassTransit's core features only; none depends on Courier, job consumers or riders.
- A-3: One or two engineers are available for about 20 weeks for v1.
- A-4: Products can accept at-least-once delivery with idempotent consumers.
- A-5: New messages can use the new wire format; sharing queues with MassTransit services is needed only during migration (v2 adapter).

### Constraints

- C-1: Licence must be MIT, with no copyleft or commercial runtime dependency.
- C-2: Runtime targets are .NET 10; Native AOT and trimming must work.
- C-3: The core package has no third-party dependencies; each external library is isolated in its own package.
- C-4: Public API names follow MassTransit v8 where a feature exists in both.
- C-5: No breaking change to wire format or public API within a major version.
- C-6: Maintenance must fit one or two part-time engineers, which caps scope.

### Dependencies

| Dependency | Needed for | Risk if unavailable |
| --- | --- | --- |
| RabbitMQ 3.13+ or 4.x with quorum queues | RabbitMQ transport | Use PostgreSQL transport |
| RabbitMQ delayed-message plugin | Native delay on RabbitMQ | Falls back to TTL plus dead-letter, or outbox scheduler |
| PostgreSQL 14+ | PostgreSQL transport, outbox, inbox, sagas | No fallback; required |
| EF Core 8 or 10, Npgsql | Storage providers | Raw Npgsql provider covers non-EF apps |
| Modulus framework | Integration package and CLI | Bus still works standalone |
| Separate mediator package | Domain-event to integration-event conversion | Publish integration events directly |
| Testcontainers, Toxiproxy | Conformance and chaos suites | Release gates cannot be verified |

## 7. Business requirements

Fourteen business requirements state what the organisation needs, independent of how the bus is built.

| ID | Requirement | Priority | Rationale |
| --- | --- | --- | --- |
| BR-01 | The bus shall be usable in commercial products at no licence cost, under MIT. | Must | OBJ-1 |
| BR-02 | The bus shall provide the MassTransit core features the portfolio uses, so no product loses capability. | Must | OBJ-2 |
| BR-03 | A business event shall be sent if and only if the business transaction that produced it commits. | Must | OBJ-3 |
| BR-04 | A business event shall take effect once in each consumer, even when delivered more than once. | Must | OBJ-3 |
| BR-05 | A failed message shall never be lost silently; it shall be retried, then parked where an operator can inspect and replay it. | Must | OBJ-3 |
| BR-06 | Long-running business processes (order fulfilment, procurement approval, billing cycles) shall be modelled, persisted and resumed after a restart. | Must | OBJ-2 |
| BR-07 | One tenant's messages shall never be processed in another tenant's data scope. | Must | OBJ-6 |
| BR-08 | A modular monolith shall run durable messaging with no infrastructure beyond its PostgreSQL database. | Must | OBJ-5 |
| BR-09 | The same application code shall run on any supported transport, changed by configuration only. | Must | OBJ-5 |
| BR-10 | Existing MassTransit code shall migrate with mostly mechanical changes (namespaces and registration). | Must | OBJ-4 |
| BR-11 | Every feature shall be configurable per bus, endpoint, consumer, saga, message and call, and tunable by operations without a rebuild. | Must | OBJ-6 |
| BR-12 | Operations shall see the health, throughput, delay and failures of messaging in standard monitoring tools. | Must | OBJ-3 |
| BR-13 | Developers shall test consumers and sagas quickly and deterministically without a broker. | Should | OBJ-4 |
| BR-14 | Services still on MassTransit or Rebus shall exchange messages with migrated services during a gradual migration. | Could (v2) | OBJ-2 |

## 8. Functional requirements

v1 has 25 functional requirements and v2 adds 8; each has a priority and an acceptance criterion that a test can verify.

### v1

| ID | Requirement | Priority | Acceptance criterion |
| --- | --- | --- | --- |
| FR-01 | Publish an event to every subscribed endpoint, including subscribers of its interfaces and base types. | Must | Two services subscribed to `OrderSubmitted` and one to `IOrderEvent` each receive one copy. |
| FR-02 | Send a command to one named endpoint, by address or by endpoint convention. | Must | A command sent to `queue:billing.create-invoice` is consumed by exactly one instance. |
| FR-03 | Consume messages through `IConsumer<T>` with a DI scope per message and a `ConsumerDefinition<T>` for endpoint settings. | Must | A consumer resolves a scoped `DbContext`; settings in its definition apply. |
| FR-04 | Consume messages in batches by size and time window. | Should | 250 messages with limit 100 and 1 s window arrive as batches of 100, 100, 50. |
| FR-05 | Consume `Fault<T>` events after retries are exhausted. | Must | A failing consumer produces one `Fault<T>` that a fault consumer receives with exception details. |
| FR-06 | Request/response with typed and multiple response types, timeout and remote fault. | Must | Client receives `CreditApproved` or `CreditRejected`; timeout and remote exception surface as typed exceptions. |
| FR-07 | Immediate retry with Immediate, Interval, Intervals, Incremental and Exponential policies and exception filters. | Must | A transient failure succeeds on attempt 3; an ignored exception goes straight to `_error`. |
| FR-08 | Delayed redelivery by policy and by `ctx.Redeliver(delay)`. | Must | A message is redelivered after each configured interval with an incremented redelivery count. |
| FR-09 | Move failed messages to `_error` and unhandled ones to `_skipped`, with fault headers, and replay them to the source queue. | Must | A poison message lands in `_error` with exception headers and is replayed by API and CLI. |
| FR-10 | Transactional outbox (bus and consumer) on EF Core and Npgsql; in-memory outbox. | Must | Rolling back the transaction sends nothing; committing sends each message once. |
| FR-11 | Idempotent inbox per consumer with a configurable detection window. | Must | A message delivered twice changes data once; the duplicate counter increments. |
| FR-12 | Schedule send and publish for a future time and cancel by token. | Must | A message scheduled for +15 min arrives then; a cancelled one never arrives. |
| FR-13 | Message time-to-live and priority. | Should | An expired message goes to `_skipped` unprocessed; higher priority is consumed first on RabbitMQ. |
| FR-14 | Ordered processing by partition key or strict FIFO. | Should | Messages with the same key are processed in send order under concurrency 16. |
| FR-15 | Concurrency limit, prefetch, rate limiter, kill switch and circuit breaker. | Must | With a 15% failure rate the kill switch pauses the endpoint and restarts it after the timeout. |
| FR-16 | Per-consumer consume timeout and graceful shutdown that drains in-flight messages. | Must | On stop, in-flight messages finish or are redelivered; none is lost. |
| FR-17 | State machine sagas with states, events, correlation, Then, TransitionTo, Publish, Send, Request, Schedule, If, Catch, Ignore, Finalize. | Must | The order saga sample completes its happy path, timeout path and cancel path. |
| FR-18 | Consumer sagas with `InitiatedBy`, `Orchestrates`, `InitiatedByOrOrchestrates`. | Should | A consumer saga is created by its initiating message and updated by later ones. |
| FR-19 | Saga repositories on EF Core, Npgsql JSONB and InMemory with optimistic or pessimistic concurrency and query correlation. | Must | Two concurrent events for one saga both apply, with no lost update. |
| FR-20 | Filters (send, publish, consume; scoped or singleton), a transaction filter, and observers for bus, receive, consume, publish, send. | Must | A custom filter and an observer both see every message at their point. |
| FR-21 | Multi-tenancy: tenant id flows from producer to consumer and saga scope; RLS-aware. | Must | A consumer for tenant A cannot read tenant B rows; a tenant-scoped consumer rejects a message with no tenant. |
| FR-22 | Payload encryption (AES-256-GCM) and a body size limit. | Should | An encrypted body is unreadable on the broker and decrypts in the consumer; an oversize message is rejected at send. |
| FR-23 | Topology by convention, `DescribeTopology()`, deploy-only mode, endpoint name formatters. | Must | Topology is created idempotently; deploy-only mode exits after declaring it. |
| FR-24 | OpenTelemetry traces and metrics, structured logs, health checks. | Must | One trace spans producer, outbox and consumer; health reports unhealthy when the broker is down. |
| FR-25 | Test harness with saga harness, virtual clock and fault injection; transport conformance kit. | Must | A saga timeout test passes in under 1 s by advancing the virtual clock. |

### Configuration (applies to every FR)

| ID | Requirement | Priority | Acceptance criterion |
| --- | --- | --- | --- |
| FR-C1 | Each feature is configurable at bus, endpoint, consumer or saga, message type and call scope; the narrowest scope wins. | Must | A consumer-level retry overrides the bus-level retry for that consumer only. |
| FR-C2 | Settings can be bound from appsettings and override code at the same scope. | Must | Changing `ConcurrentMessageLimit` in appsettings takes effect on restart with no rebuild. |
| FR-C3 | Defaults match MassTransit: retry, redelivery, outbox, inbox and scheduler are off until configured; error queues and faults are on. | Must | An unconfigured bus moves a failing message to `_error` on first failure. |
| FR-C4 | A feature that is not configured adds no per-message cost. | Should | Benchmark overhead is unchanged when unused filters are registered but not enabled. |

### v2

| ID | Requirement | Priority | Acceptance criterion |
| --- | --- | --- | --- |
| FR-26 | MassTransit wire adapter (read and write MassTransit envelopes). | Could | A MassTransit v8 service and a migrated service exchange messages both ways on shared queues. |
| FR-27 | Rebus wire adapter (map `rbs2-*` headers). | Could | A Rebus saga round-trips with a consumer on this bus. |
| FR-28 | Azure Service Bus transport. | Could | Passes the unchanged v1 conformance suite. |
| FR-29 | Amazon SQS/SNS transport. | Could | Passes the unchanged v1 conformance suite. |
| FR-30 | Error-queue dashboard and messaging CLI (inspect, replay, discard, purge, topology diff). | Could | An operator replays a failed message without database access. |
| FR-31 | AsyncAPI 3 generation and saga diagram export. | Could | Build produces an AsyncAPI file and a Mermaid diagram per state machine. |
| FR-32 | Redis saga repository and composite events. | Could | A composite event fires once all its source events have arrived. |
| FR-33 | Push-based outbox dispatch and PostgreSQL transport priority. | Could | Dispatch latency drops below the polling interval; priority order is honoured. |

## 9. Non-functional requirements

The bus must be reliable first, then fast, then small; the targets below are design targets to be confirmed by the benchmark and chaos suites.

| ID | Category | Requirement | Target | Priority |
| --- | --- | --- | --- | --- |
| NFR-01 | Reliability | Delivery guarantee | At-least-once; 0 lost and 0 double-applied messages in a 1M-message chaos run | Must |
| NFR-02 | Reliability | Recovery after broker or database outage | Automatic reconnect with backoff from 1 s to 60 s; no manual restart | Must |
| NFR-03 | Performance | In-memory throughput | 1M msg/s or more on 8 cores, 200-byte payload | Should |
| NFR-04 | Performance | RabbitMQ throughput | 25k msg/s or more per host with publisher confirms | Must |
| NFR-05 | Performance | PostgreSQL transport throughput | 2k to 5k msg/s per database | Must |
| NFR-06 | Performance | Bus overhead per message | Under 5 µs p50; under 2 allocations beyond payload | Should |
| NFR-07 | Performance | Startup | Under 50 ms for 100 consumers | Should |
| NFR-08 | Scalability | Horizontal scale | Competing consumers and outbox dispatch safe on any number of instances | Must |
| NFR-09 | Security | Transport security | TLS to every broker; credentials from configuration or a secret store only | Must |
| NFR-10 | Security | Deserialization safety | Only registered message types are deserialized; no type name trusted from the wire | Must |
| NFR-11 | Security | Least privilege | Apps run with publish/consume rights; topology deployed separately with admin rights | Should |
| NFR-12 | Security | Sensitive data | `[Sensitive]` properties redacted in logs and traces | Should |
| NFR-13 | Compatibility | Runtime | .NET 10; Native AOT and trimming safe | Must |
| NFR-14 | Compatibility | Stability | No breaking wire or public API change within a major version; API baseline enforced in CI | Must |
| NFR-15 | Maintainability | Size | Core under 6k lines and under 150 KB; zero third-party dependencies in core | Should |
| NFR-16 | Maintainability | Test coverage | 85% or more line coverage on core; conformance suite per transport and per saga repository | Must |
| NFR-17 | Observability | Standards | OpenTelemetry messaging semantic conventions for spans and metrics | Must |
| NFR-18 | Operability | Shutdown | In-flight messages drained within the stop timeout (default 30 s) | Must |
| NFR-19 | Usability | Migration | A MassTransit v8 developer is productive within 1 hour using the migration guide | Should |
| NFR-20 | Usability | Documentation | Docs site with quick start per transport, configuration reference, migration guide, samples | Must |
| NFR-21 | Licensing | Licence | MIT; signed NuGet packages with SourceLink and symbols | Must |

## 10. Business rules

These rules hold in every product and are not configurable away without an explicit, documented opt-out.

| ID | Rule |
| --- | --- |
| RULE-01 | When an outbox is configured, a message published inside a transaction is sent only if that transaction commits. |
| RULE-02 | A delivery is acknowledged to the broker only after the consumer's transaction has committed. |
| RULE-03 | A message that fails after all retries and redeliveries is moved to the error queue; it is never dropped unless the endpoint is explicitly set to discard faulted messages. |
| RULE-04 | With the inbox enabled, a message id is applied at most once per consumer within the duplicate-detection window. |
| RULE-05 | A message keeps the tenant of the context that produced it; a tenant-scoped consumer rejects a message with no tenant. |
| RULE-06 | Saga state, the inbox row and outgoing messages are committed in one transaction. |
| RULE-07 | Each retry attempt runs in a new DI scope with a fresh `DbContext`. |
| RULE-08 | An expired message is never passed to user code. |
| RULE-09 | Request/response messages are not captured by the outbox. |
| RULE-10 | Message identity on the wire is a URN; renaming a .NET type must not change the URN of a released message. |
| RULE-11 | Breaking changes to a message contract require a new URN version, with both versions published during migration. |
| RULE-12 | A module consumes only another module's public contract types, never its internal types. |
| RULE-13 | Message headers are never treated as proof of identity or authorisation. |
| RULE-14 | The narrowest configuration scope wins: call, then message, then consumer or saga, then endpoint, then bus. |

## 11. Use cases

Ten use cases cover what developers and operators do with the bus; each lists its main flow and the exceptions that matter.

| ID | Use case | Actor | Main flow | Exceptions | Requirements |
| --- | --- | --- | --- | --- | --- |
| UC-01 | Publish an integration event with business data | Developer | Handler saves the aggregate and publishes; the event is stored in the outbox in the same transaction; the dispatcher sends it after commit. | Transaction rolls back: nothing is sent. Broker down: rows wait and are sent on recovery. | FR-01, FR-10 |
| UC-02 | Consume an event idempotently | Developer | Message arrives; inbox row inserted; consumer runs; transaction commits; delivery acked. | Duplicate: acked and skipped. Exception: retried, then redelivered, then moved to `_error`. | FR-03, FR-07, FR-08, FR-11 |
| UC-03 | Send a command to another module | Developer | Handler sends to a named endpoint; exactly one instance consumes it. | No consumer for the type on that queue: message goes to `_skipped`. | FR-02 |
| UC-04 | Query another service | Developer | Client sends a request and awaits a typed response. | Timeout: typed exception; responder skips the expired request. Remote failure: fault returned to caller. | FR-06 |
| UC-05 | Run a long-running process | Developer | Initiating event creates a saga; later events move it through states; a schedule guards against a missing step; `Finalize` completes it. | Timeout fires: compensation published. Concurrent events: retried on conflict. Event before start: redelivered. | FR-17, FR-19, FR-12 |
| UC-06 | Recover failed messages | Operator | Operator sees the `_error` alert, inspects fault headers, fixes the cause, replays the messages to the source queue. | Message is invalid: operator discards it. | FR-09, FR-24 |
| UC-07 | Protect a failing dependency | Operator | Failure rate crosses the threshold; the kill switch pauses the endpoint; it restarts after the timeout. | Still failing: it trips again; alert is raised. | FR-15 |
| UC-08 | Onboard a tenant on a dedicated queue | Operator | Operator enables dedicated isolation for the tenant; topology is deployed; the tenant's messages flow through its own queue. | Deployment lacks admin rights: deploy-only job runs in CI. | FR-21, FR-23 |
| UC-09 | Migrate a module from MassTransit | Developer | Change package references and namespaces; swap registration; run harness tests; deploy. | Module uses an excluded feature: redesign or defer. Mixed fleet needed: wait for the v2 adapter. | BR-10, FR-25 |
| UC-10 | Move a monolith to a broker | Operator | Change the transport from PostgreSQL to RabbitMQ in configuration; deploy topology; restart. | In-flight messages on the old transport: drain before the switch. | BR-09, FR-23 |

## 12. Data, interface and configuration requirements

The bus owns five kinds of data, exposes four kinds of interface, and must be configurable at six scopes.

### Data

| Data | Content | Retention | Owner |
| --- | --- | --- | --- |
| Outbox messages | Destination, headers, body, tenant, deliver-at time | Deleted after confirmed send | Producing service's database |
| Inbox state | Message id, consumer id, received and consumed times | Duplicate-detection window, default 7 days | Consuming service's database |
| Saga state | Correlation id, current state, version, business fields, tenant | Until finalized, or kept for audit if configured | Service that owns the saga |
| Transport messages (PostgreSQL) | Queue, headers, body, visibility time, delivery count | Until acknowledged | Shared messaging schema |
| Error and skipped messages | Original message plus fault headers | Until replayed or discarded by an operator | Each endpoint |

- Message bodies may contain personal data; products must mark such fields `[Sensitive]` and enable encryption where their compliance rules require it.
- Schema changes ship as versioned, additive SQL migration scripts.

### Interfaces

| Interface | Requirement |
| --- | --- |
| Developer API | MassTransit-style contracts: `IBus`, `IPublishEndpoint`, `ISendEndpointProvider`, `IConsumer<T>`, `IRequestClient<T>`, `IMessageScheduler`, `SagaStateMachine<T>`, `ISaga`. |
| Wire format | Metadata in transport headers (`cb-*`, W3C trace headers); body is bare JSON by default; readable by non-.NET services. |
| Brokers and databases | RabbitMQ over AMQP 0-9-1 with TLS; PostgreSQL through Npgsql; cloud transports in v2. |
| Operations | OpenTelemetry (OTLP exporters), ASP.NET Core health checks, structured logs, a diagnostics endpoint, CLI for replay and topology. |

### Configuration

| Scope | Set by | Example need |
| --- | --- | --- |
| Bus | Developer | Default retry for all endpoints |
| Endpoint | Developer, operator | Concurrency and prefetch for one queue |
| Consumer or saga | Developer | Timeout, partitioning, own retry policy |
| Message type | Developer | Entity name, default time-to-live |
| Call | Developer | Priority or expiry for one message |
| appsettings | Operator | Tune limits and intervals without a rebuild |

## 13. Acceptance and release criteria

1.0 is accepted when all Must requirements pass their acceptance criteria, the five v1 gates are green in CI, and one product has run on the bus in production for two weeks.

### v1 gates

| Gate | After phase | Criterion |
| --- | --- | --- |
| G0 Conformance green | M0 Foundations (weeks 1–4) | Shared transport suite passes on InMemory; generator handles every consumer shape in the samples |
| G1 Zero loss in chaos | M1 Reliability (weeks 5–8) | 1M messages with random app, broker and database kills: none lost, none double-applied |
| G2 Throughput | M2 RabbitMQ (weeks 9–12) | 25k msg/s on RabbitMQ with confirms; bus overhead under 5 µs p50 |
| G3 Sample on PostgreSQL | M3 Postgres (weeks 13–17) | TradeFlow sample runs orders to billing on the PostgreSQL transport with the order saga, outbox and tenancy |
| G4 Production pilot | M4 Release (weeks 18–20) | One portfolio product in production for two weeks with no Sev-1 incident |

### Definition of done for 1.0

- [ ] Every Must FR and NFR has a passing automated test linked to its ID.
- [ ] Conformance suites pass for all three transports and all three saga repositories.
- [ ] Benchmark gate in CI: no regression above 10% in throughput or allocations.
- [ ] AOT-published sample builds and runs with no trim warnings.
- [ ] Public API baseline reviewed and frozen.
- [ ] Documentation site, migration guide and samples published.
- [ ] Security review complete; `SECURITY.md` and disclosure policy in place.
- [ ] Packages signed and published to NuGet under MIT.
- [ ] Product owner sign-off recorded in section 16.

### v2 entry and acceptance

- Entry: 1.0 in two products for three months with no Sev-1 incident; no open data-loss or duplicate bug; nightly suites green.
- Acceptance: each v2 FR passes its criterion; new transports pass the unchanged v1 conformance suite; no v1 wire or API contract changes.

## 14. Risks

The largest business risk is taking on ownership of critical infrastructure; the mitigations keep scope narrow and prove correctness by test before any product depends on it.

| ID | Risk | Likelihood | Impact | Mitigation | Owner |
| --- | --- | --- | --- | --- | --- |
| R-01 | Maintenance load exceeds one or two part-time engineers | High | High | Fixed non-goals; demand-driven backlog; API baseline | Product owner |
| R-02 | A reliability bug loses or duplicates business events | Medium | High | Chaos suite as a release gate; pilot before wide rollout | Architect |
| R-03 | v1 slips past 20 weeks | Medium | Medium | Gated phases; Should items can move to 1.1 | Product owner |
| R-04 | Building costs more than licensing MassTransit v9 | Medium | Medium | Review cost against confirmed v9 pricing at gate G1; stop or continue decision | Product owner |
| R-05 | PostgreSQL transport overloads a product's primary database | Medium | Medium | Documented ceiling; move to RabbitMQ by configuration | Operations |
| R-06 | Products need to share queues with MassTransit before the v2 adapter exists | Medium | Medium | Migrate whole services at once; bring adapter forward if needed | Architect |
| R-07 | Developers resist a non-standard bus | Medium | Low | MassTransit-compatible names; migration guide; samples | Architect |
| R-08 | Key-person dependency on the bus's author | Medium | High | Design docs, ADRs, conformance tests, second reviewer on every change | Product owner |
| R-09 | Managed RabbitMQ lacks the delayed-message plugin | Medium | Low | Automatic fallback to TTL plus dead-letter or outbox scheduler | Operations |
| R-10 | Security defect in an in-house component | Low | High | Registered-types-only deserialization; security review; disclosure policy | Architect |

## 15. Traceability matrix

Every business requirement traces up to an objective and down to the functional and non-functional requirements, use cases and design sections that satisfy it.

| Business requirement | Objective | Functional | Non-functional | Use cases | Design section |
| --- | --- | --- | --- | --- | --- |
| BR-01 Licence-free | OBJ-1 | All | NFR-21 | UC-09 | 1, 17 |
| BR-02 Core feature parity | OBJ-2 | FR-01 to FR-25 | NFR-14 | UC-01 to UC-05 | 2, 4 |
| BR-03 Send only on commit | OBJ-3 | FR-10 | NFR-01 | UC-01 | 9 |
| BR-04 Apply once | OBJ-3 | FR-11 | NFR-01 | UC-02 | 9 |
| BR-05 No silent loss | OBJ-3 | FR-05, FR-07, FR-08, FR-09, FR-15, FR-16 | NFR-01, NFR-02, NFR-18 | UC-02, UC-06, UC-07 | 6, 9 |
| BR-06 Long-running processes | OBJ-2 | FR-12, FR-17, FR-18, FR-19 | NFR-16 | UC-05 | 11, 12 |
| BR-07 Tenant isolation | OBJ-6 | FR-21, FR-22 | NFR-09, NFR-12 | UC-08 | 13 |
| BR-08 No broker for a monolith | OBJ-5 | FR-10, FR-12 (PostgreSQL transport) | NFR-05 | UC-10 | 8 |
| BR-09 Transport by configuration | OBJ-5 | FR-23 | NFR-08 | UC-10 | 7, 8 |
| BR-10 Cheap migration | OBJ-4 | FR-03, FR-06, FR-17, FR-C3 | NFR-19, NFR-20 | UC-09 | 4 |
| BR-11 Configurable everywhere | OBJ-6 | FR-C1 to FR-C4 | NFR-06 | UC-07, UC-08 | 4 |
| BR-12 Visible in monitoring | OBJ-3 | FR-24, FR-20 | NFR-17 | UC-06 | 14 |
| BR-13 Fast deterministic tests | OBJ-4 | FR-25 | NFR-16 | UC-09 | 15 |
| BR-14 Gradual migration (v2) | OBJ-2 | FR-26, FR-27 | NFR-14 | UC-09 | 18 |

FR-02, FR-04, FR-13 and FR-14 support BR-02 and have no separate business requirement. FR-28 to FR-33 serve OBJ-5 and OBJ-7 in v2.

## 16. Glossary, open issues and sign-off

### Glossary

| Term | Meaning |
| --- | --- |
| Publish | Deliver an event to every subscriber |
| Send | Deliver a command to one named endpoint |
| Consumer | Class that handles one message type |
| Receive endpoint | A queue plus the consumers bound to it |
| Outbox | Table that holds outgoing messages inside the business transaction until they are sent |
| Inbox | Table that records processed message ids so duplicates are skipped |
| Saga | Persisted, correlated process that reacts to several messages over time |
| Redelivery | Returning a message to its queue to be tried again after a delay |
| Error queue | Queue (`_error`) that holds messages which failed all attempts |
| Skipped queue | Queue (`_skipped`) that holds messages no consumer handled or that expired |
| Topology | The exchanges, topics, queues and bindings derived from message and consumer types |
| URN | Stable wire name of a message type |
| Kill switch | Pauses an endpoint when its failure rate crosses a threshold |
| Conformance suite | Shared tests every transport and saga repository must pass |
| RLS | PostgreSQL row-level security |

### Open issues

- [ ] Confirm current MassTransit v9 pricing to settle the build-versus-buy check at gate G1 (R-04).
- [ ] Choose the pilot product for gate G4.
- [ ] Confirm the final package name; "Cobytelabs.Messaging" is a working name.
- [ ] Decide whether to publish as open source at 1.0 or after hardening in the portfolio.
- [ ] Decide which cloud transport comes first in v2: Azure Service Bus or Amazon SQS/SNS.
- [ ] Confirm the engineers and weekly capacity assigned to v1 (A-3).

### Sign-off

| Role | Name | Decision | Date |
| --- | --- | --- | --- |
| Product owner |  |  |  |
| Architect |  |  |  |
| Engineering lead |  |  |  |
| Operations lead |  |  |  |
| QA lead |  |  |  |

### Sources

- [Announcing MassTransit v9](https://masstransit.io/introduction/v9-announcement)
- [MassTransit 9.1.1 on NuGet](https://www.nuget.org/packages/MassTransit/9.1.1)
- [MediatR and MassTransit going commercial (timeline and pricing)](https://milanjovanovic.tech/blog/mediatr-and-masstransit-going-commercial-what-this-means-for-you)
- [Cobytelabs.Messaging system design](https://claude.ai/artifact/LnRX2AeEnQWReoeamQXLTy)
