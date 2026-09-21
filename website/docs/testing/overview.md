---
sidebar_position: 1
---

# Testing Overview

Modulus provides a comprehensive testing harness.

## Test Framework

| Tool | Purpose |
|------|---------|
| **xUnit** | Test framework |
| **NSubstitute** | Mocking |
| **FluentAssertions** | Assertion library |
| **Testcontainers** | Integration test infrastructure |

## Unit Tests

```csharp
[Trait("Category", "Unit")]
public sealed class CreateProductHandlerTests
{
    private readonly IProductRepository _repo = Substitute.For<IProductRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CreateProductHandler _sut;

    public CreateProductHandlerTests()
    {
        _sut = new CreateProductHandler(_repo, _unitOfWork);
    }

    [Fact]
    public async Task HandleAsync_CreatesProduct()
    {
        // Arrange
        var command = new CreateProductCommand("Widget");

        // Act
        var id = await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        await _repo.Received(1).AddAsync(
            Arg.Is<Product>(p => p.Name == "Widget"),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }
}
```

## Running Tests

```bash
# All tests
dotnet test modulus.slnx

# Unit tests only
dotnet test modulus.slnx --filter "Category=Unit"

# Integration tests (requires Docker)
dotnet test modulus.slnx --filter "Category=Integration"

# Specific project
dotnet test tests/unit/Modulus.Core.Tests
```

## Test Projects

| Project | Coverage |
|---------|----------|
| `Modulus.Core.Tests` | Module system, DDD primitives |
| `Modulus.AspNetCore.Tests` | Idempotency, OpenAPI, feature flags, secrets guard |
| `Modulus.EntityFrameworkCore.Tests` | DbContext, repositories, PII encryption |
| `Modulus.Events.Tests` | Integration event naming, trace context |
| `Modulus.EventBus.RabbitMQ.Tests` | RabbitMQ publisher confirms, durability |
| `Modulus.EventBus.Kafka.Tests` | Kafka partition distribution, offset management |
| `Modulus.Inbox.Tests` / `Modulus.Inbox.MongoDB.Tests` | Inbox deduplication (EF + Mongo) |
| `Modulus.Outbox.Tests` / `Modulus.Outbox.MongoDB.Tests` | Outbox processor, at-least-once delivery |
| `Modulus.Caching.Redis.Tests` | Redis cache backend |
| `Modulus.Mediator.Tests` | Pipeline behaviors |
| `Modulus.MultiTenancy.Tests` | Tenant store, migration fan-out |
| `Modulus.Platform.Tests` | Platform services |
| `Modulus.Authorization.*.Tests` | Grants, management API |
| `Modulus.Sagas.Tests` | Saga orchestration |
| `Modulus.BackgroundJobs.Quartz.Tests` | Quartz scheduling |
| `Modulus.Identity.Tests` | Password grant, external IdP validation, account endpoints |
| `Modulus.Observability.Tests` | OTel bootstrap wiring |
| `Modulus.Testing.Tests` | Test harness, WebApplicationFactory |
| `Modulus.Cli.Tests` | Scaffolding, template rendering |

## Test Doubles

`Modulus.Testing` ships a recording bus plus event assertions for
integration tests (register `RecordingModuleBus` as the `IModuleBus` in the
test host):

```csharp
var bus = factory.Services.GetRequiredService<RecordingModuleBus>();
var published = bus.PublishedEvents<ProductCreatedIntegrationEvent>();
published.Should().ContainSingle(e => e.Id == productId);
bus.Clear();
```

**Real test seams:** `RecordingModuleBus` (+ event assertions),
`ModuleBoundaryRules.FindUnnamedIntegrationEvents()` /
`FindModuleTypes()` for architecture tests. There is no `.WithFake<T>()`
chain, `FakeCurrentTenant/User`, or `ModuleTestFixture` — substitute
NSubstitute mocks or register test doubles in DI directly.

## Event Assertions

Test event-driven behavior with built-in helpers:

```csharp
[Fact]
public async Task CreateProduct_PublishesEvent()
{
    var bus = factory.Services.GetRequiredService<RecordingModuleBus>();
    var client = factory.CreateClient();

    await client.PostAsync("/api/catalog/products",
        JsonContent.Create(new { name = "Widget" }));

    bus.PublishedEvents<ProductCreatedIntegrationEvent>()
        .Should().ContainSingle();
}
```

## Architecture Rules

Enforce module boundaries in tests:

```csharp
[Fact]
public void AllIntegrationEvents_HaveExplicitNames()
{
    var unnamed = ModuleBoundaryRules.FindUnnamedIntegrationEvents();
    unnamed.Should().BeEmpty(
        "Integration events must carry [IntegrationEventName]");
}

[Fact]
public void AllModules_CanBeInstantiated()
{
    var modules = ModuleBoundaryRules.FindModuleTypes();
    modules.Should().NotBeEmpty();
}
```

## See Also

- [Integration Tests](integration-tests) — WebApplicationFactory harness
- [Features: Testing & DX](../features#testing--dx-package-e4) — Test doubles and assertions
