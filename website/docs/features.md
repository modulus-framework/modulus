---
sidebar_position: 2
---

# Framework Features

Modulus provides a comprehensive set of features for building scalable, production-ready modular-monolith applications.

## Observability & Messaging (E1)

### Distributed Trace Context Propagation

Traces now flow seamlessly across async boundaries and message brokers. W3C `TraceParent` and `TraceState` are carried on:
- Integration event envelopes
- Outbox messages
- Message broker headers (RabbitMQ, Kafka)

```csharp
// Spans automatically continue across message boundaries
using var activity = new ActivitySource("MyApp").StartActivity("ProcessOrder");
await bus.PublishAsync(new OrderCreatedEvent { ... });
// Consumer automatically restores parent trace context
```

### Message Durability

**RabbitMQ:**
- Publisher confirms ensure messages reach the broker
- Persistent delivery mode survives broker restarts
- Auto-recovery on connection loss
- Full message metadata (MessageId, CorrelationId, Headers)

**Kafka:**
- At-least-once semantics (EnableAutoCommit = false by default)
- Partition key distribution for aggregate-level ordering
- Configurable via `IPartitionKeyProvider`

### Pluggable Message Serialization

Single, unified serializer for all framework messages:
- `IMessageSerializer` interface (Serialize/Deserialize)
- `SystemTextJsonMessageSerializer` default (camelCase, case-insensitive)
- `SchemaVersion` on envelopes for upcasting paths
- Configured via `AddModulusEvents()` registration

```csharp
services.AddModulusEvents(typeof(Program).Assembly);
// Automatically registers IMessageSerializer with framework defaults
```

### Performance Metrics

New histogram instruments for observability:
- **Mediator:** Handler duration (milliseconds)
- **Outbox:** Dispatch lag (created → dispatched)
- **Caching:** Hit/miss counters, lookup duration
- **Authorization:** Decision duration per policy
- **Module init:** Per-module startup time

Access via `ModulusMeters.Mediator`, `ModulusMeters.Caching`, `ModulusMeters.Authorization`.

### Causation Tracking

Track event chains across service boundaries:
- `ICausationIdContext` ambient accessor
- Automatically set from consumed envelope's `EventId`
- Stamps outbox messages with causation chain
- Enables root-cause analysis and debugging

### Domain Event Error Isolation

Events now fail independently:
- Per-handler try/catch collection
- One handler throwing doesn't block others
- `AggregateException` if multiple handlers fail
- Configurable via `DomainEventDispatchMode` (BeforeSave/AfterSave)

---

## Data Layer Depth (E2)

### Composable Specifications

Build queries as objects with combinators:

```csharp
public sealed class ProductsByCategorySpec : Specification<Product>
{
    public ProductsByCategorySpec(string category)
    {
        Filter = p => p.Category == category;
        AddOrderBy(p => p.CreatedAt);
        AddInclude(p => p.Category);
        AsSplitQuery = true; // Prevent cartesian explosion
    }
}

// Combinators for complex predicates (mutate + return the spec)
var spec2 = baseSpec.And(p => p.Active).Or(p => p.Featured).Not();
```

Also available: `IgnoreQueryFilters`, `Tag`, `AsNoTracking`, `Skip`/`Take`.

### Server-Side Projection

Project directly in the database query:

```csharp
// Expression-based projection (executes in SQL) — spec first, then selector
var dtos = await repo.ListPagedAsync(
    spec,
    x => new ProductDto
    {
        Id = x.Id,
        Name = x.Name // Joined in SQL, not LINQ-to-Objects
    },
    page: 1, size: 10, ct);
```

### New Repository Methods

- `FirstOrDefaultAsync(spec)` — Single row with default
- `SingleAsync(spec)` — Single row (throws if 0 or 2+)
- `SingleOrDefaultAsync(spec)` — Single row or null
- `AsAsyncEnumerable(spec)` — Streaming large result sets
- `DeleteRangeAsync(spec)` — Bulk delete matching a spec, filters respected
- `GetByIdAsync(object id, ct)` — Filter-honoring lookup, composite-PK aware

### Specification Validation

Paging requires ordering to be deterministic (`Skip`/`Take` without an
`OrderBy` clause throws `InvalidOperationException`):

```csharp
public sealed class PagedSpec : Specification<Product>
{
    public PagedSpec()
    {
        AddOrderBy(p => p.Id);
        Skip = 10;
        Take = 20;
    }
}
// ✓ Valid: OrderBy is set
```

---

## Test & CI Infrastructure (E3)

### Test Project Scaffolding

New test projects for previously untested packages:
- `Modulus.Events.Tests`
- `Modulus.EventBus.RabbitMQ.Tests`
- `Modulus.EventBus.Kafka.Tests`
- `Modulus.Caching.Redis.Tests`
- `Modulus.Outbox.MongoDB.Tests`
- `Modulus.Inbox.MongoDB.Tests`

All follow xUnit + NSubstitute + FluentAssertions conventions with `[Trait("Category", "Unit")]`.

### Integration Test Coverage

New `Modulus.Messaging.Integration.Tests` project:
- Publish/consume round-trips with real RabbitMQ/Kafka containers
- **Trace continuity acceptance test:** Verifies TraceId flows across broker
- Outbox at-least-once delivery with competing processors
- Broker restart durability

```csharp
[Fact]
public async Task Event_published_in_activity_is_consumed_with_same_traceId()
{
    using var activity = _source.StartActivity("TestActivity");
    var traceId = Activity.Current.TraceId;
    
    await _bus.PublishAsync(new TestEvent { ... });
    
    var consumed = await _consumer.WaitForEventAsync<TestEvent>(timeout: 5s);
    consumed.TraceId.Should().Be(traceId);
}
```

---

## Testing & DX Package (E4)

### Test Doubles

`Modulus.Testing` ships a recording bus plus event assertions for
integration tests:

```csharp
var bus = factory.Services.GetRequiredService<RecordingModuleBus>();
bus.PublishedEvents<ProductCreatedIntegrationEvent>()
    .Should().ContainSingle();
bus.Clear();
```

There is no `.WithFake<T>()` chain, `FakeCurrentTenant/User`, or
`ModuleTestFixture` — use NSubstitute mocks or register test doubles in DI
directly. Real seams: `RecordingModuleBus`, event assertions,
`ModuleBoundaryRules.FindUnnamedIntegrationEvents()` / `FindModuleTypes()`.

### Event Assertions

Built-in recording bus for event-driven test assertions:

```csharp
var bus = factory.Services.GetRequiredService<RecordingModuleBus>();

// Assert event was published
bus.PublishedEvents<OrderCreatedIntegrationEvent>().Should().HaveCount(1);

// All events, or reset between phases
bus.AllPublishedEvents.Should().NotBeEmpty();
bus.Clear();
```

### Module Smoke Test

Generated apps include a smoke test that boots the full module pipeline and
verifies every module `DbContext` resolves from DI; the integration-test
harness (`ModulusWebAppFactory<Program>`) boots the composed host with
per-context SQLite databases for HTTP round-trips (see
[Integration Tests](testing/integration-tests)):

### Architecture Rules

Enforce modular-monolith boundaries:

```csharp
[Fact]
public void AllIntegrationEvents_HaveExplicitNames()
{
    var unnamed = ModuleBoundaryRules.FindUnnamedIntegrationEvents();
    unnamed.Should().BeEmpty(
        "Integration events must carry [IntegrationEventName] " +
        "for stable serialization across module boundaries");
}

[Fact]
public void AllModules_CanBeInstantiated()
{
    var modules = ModuleBoundaryRules.FindModuleTypes();
    modules.Should().NotBeEmpty();
}
```

**Built-in rules:**
- Integration event naming validation
- Module instantiation check (no circular dependencies)
- Module type discovery for boundaries

---

## Migrations

EF Core migrations are the default; **dbsh** is the supported SQL-first
alternative (`--migration-engine dbsh`). Each EF module includes:

- `DbContextFactory` for design-time support
- Migration files in `Infrastructure/Migrations/`
- Per-module migrations scaffold via `modulus migrate add <Name>`

EF modules migrate at startup (modes above); dbsh modules are skipped via
`ExternallyManaged<TContext>` and applied with `modulus migrate update`
(`dbsh init && dbsh migrate`).

```csharp
await app.Services.MigrateModulusDatabasesAsync(
    app.Environment.IsProduction()
        ? DatabaseInitializationMode.Migrate        // Throws if no migrations
        : DatabaseInitializationMode.MigrateOrCreate // Creates schema if needed
);
```

---

## Summary

| Feature | E1 | E2 | E3 | E4 |
|---------|----|----|----|----|
| Trace Context Propagation | ✓ |  |  |  |
| Message Durability | ✓ |  |  |  |
| Message Serialization | ✓ |  |  |  |
| Performance Metrics | ✓ |  |  |  |
| Causation Tracking | ✓ |  |  |  |
| Event Error Isolation | ✓ |  |  |  |
| Composable Specs |  | ✓ |  |  |
| Server-Side Projection |  | ✓ |  |  |
| New Repository Methods |  | ✓ |  |  |
| Test Projects |  |  | ✓ |  |
| Integration Tests |  |  | ✓ |  |
| Test Doubles |  |  |  | ✓ |
| Event Assertions |  |  |  | ✓ |
| Module Smoke Test |  |  |  | ✓ |
| Architecture Rules |  |  |  | ✓ |

---

## Next Steps

- [Messaging Overview](messaging/overview) — Deep dive into E1 features
- [Data Layer](data/repositories) — Using E2 specifications and projections
- [Testing](testing/overview) — Integration and unit test patterns
- [Architecture](architecture/module-system) — Enforcing module boundaries
