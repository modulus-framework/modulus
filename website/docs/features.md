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

Build complex queries with fluent combinators:

```csharp
var spec = new Specification<Product>()
    .WithOrderBy(p => p.CreatedAt)
    .WithThenBy(p => p.Name)
    .WithInclude(p => p.Category)
    .WithInclude(p => p.Tags)
    .WithAsSplitQuery() // Prevent cartesian explosion
    .WithIgnoreQueryFilters() // Bypass tenant/soft-delete
    .WithTag("GetProductsWithDetails");

// Combinators for complex predicates
var spec2 = baseSpec.And(p => p.Active).Or(p => p.Featured).Not(p => p.Deleted);
```

### Server-Side Projection

Project directly in the database query:

```csharp
// Expression-based projection (executes in SQL)
var dtos = await repo.ListPagedAsync<ProductDto>(
    x => new ProductDto 
    { 
        Id = x.Id,
        Name = x.Name,
        CategoryName = x.Category.Name // Joined in SQL, not LINQ-to-Objects
    },
    spec, page: 1, pageSize: 10);
```

### New Repository Methods

- `FirstOrDefaultAsync(spec)` — Single row with default
- `SingleAsync(spec)` — Single row (throws if 0 or 2+)
- `SingleOrDefaultAsync(spec)` — Single row or null
- `AsAsyncEnumerable(spec)` — Streaming large result sets
- `DeleteRangeAsync(entities)` — Bulk delete with filters respected
- `ExecuteUpdateAsync(spec)` — Bulk update preserving soft-delete filters

### Specification Validation

Paging now requires ordering to be deterministic:

```csharp
var spec = new Specification<Product>()
    .WithOrderBy(p => p.Id)
    .WithSkip(10)
    .WithTake(20);
// ✓ Valid: OrderBy is set

var badSpec = new Specification<Product>()
    .WithSkip(10)
    .WithTake(20);
// ✗ Throws: Skip/Take require OrderBy
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

### Test Doubles (Fakes)

Pre-built fakes for ambient services, ready to inject:

```csharp
var factory = new ModulusWebAppFactory<Program>()
    .WithFake<ICurrentTenant>(new FakeCurrentTenant { Id = Guid.NewGuid() })
    .WithFake<ICurrentUser>(new FakeCurrentUser { Id = "user123", Roles = ["Admin"] })
    .WithFake<IModuleBus>(new RecordingModuleBus());

var client = factory.CreateClient();
```

**Fakes included:**
- `FakeCurrentTenant` — Settable tenant context
- `FakeCurrentUser` — Settable user + roles
- `FakePermissionRegistry` — Mock permission checks
- `FakeFeatureGate` — Feature flag toggles
- `FakeCacheService` — In-memory cache with inspection
- `FakeDistributedLock` — Mock distributed lock
- `RecordingModuleBus` — Captures published events
- `RecordingOutboxWriter` — Captures outbox entries

### Event Assertions

Built-in helpers for event-driven test assertions:

```csharp
var bus = factory.Services.GetRequiredService<RecordingModuleBus>();

// Assert event was published
bus.GetPublishedEvents<OrderCreatedEvent>().Should().HaveCount(1);

// Assert with predicate
bus.HasPublished<OrderCreatedEvent>(e => e.OrderId == orderId).Should().BeTrue();

// Count with filter
var productEvents = bus.CountPublished<ProductCreatedEvent>(
    e => e.ProductId == productId);
```

### Module Test Fixture

Boot a single module's DI graph without the full host:

```csharp
[Collection("Catalog")]
public class CatalogModuleTests : IAsyncLifetime
{
    private readonly ModuleTestFixture<CatalogModule> _fixture = new();

    public async Task InitializeAsync() => await _fixture.InitializeAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task CreateProduct_StoresInDatabase()
    {
        var mediator = _fixture.Services.GetRequiredService<IMediator>();
        await mediator.SendAsync(new CreateProductCommand { ... });
        
        var db = _fixture.GetContext<CatalogDbContext>();
        db.Products.Should().Contain(p => p.Name == "Widget");
    }
}
```

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

## EF Core Migrations

Framework exclusively uses EF Core migrations. Each module includes:

- `DbContextFactory` for design-time support
- Migration files in `Infrastructure/Migrations/`
- Per-module migrations scaffold via `modulus migrate add <Name>`

No external migration tools required. Migrations apply at startup:

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
| Module Test Fixture |  |  |  | ✓ |
| Architecture Rules |  |  |  | ✓ |

---

## Next Steps

- [Messaging Overview](messaging/overview) — Deep dive into E1 features
- [Data Layer](data/repository) — Using E2 specifications and projections
- [Testing](testing/overview) — Integration and unit test patterns
- [Architecture](architecture/module-system) — Enforcing module boundaries
