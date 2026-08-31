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
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CreateProductHandler _sut;

    public CreateProductHandlerTests()
    {
        _sut = new CreateProductHandler(_unitOfWork);
    }

    [Fact]
    public async Task HandleAsync_CreatesProduct()
    {
        // Arrange
        var command = new CreateProduct("Widget", 9.99m);

        // Act
        var result = await _sut.HandleAsync(command);

        // Assert
        result.Name.Should().Be("Widget");
        result.Price.Should().Be(9.99m);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
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
| `Modulus.Inbox.Tests` | Inbox deduplication |
| `Modulus.Outbox.Tests` | Outbox processor, at-least-once delivery |
| `Modulus.Caching.Redis.Tests` | Redis cache backend |
| `Modulus.Mediator.Tests` | Pipeline behaviors |
| `Modulus.Identity.Tests` | Password grant, external IdP validation |
| `Modulus.Testing.Tests` | Test harness, WebApplicationFactory |

## Test Doubles

Pre-built fakes for dependency injection in tests:

```csharp
var factory = new ModulusWebAppFactory<Program>()
    .WithFake<ICurrentTenant>(new FakeCurrentTenant { Id = Guid.NewGuid() })
    .WithFake<ICurrentUser>(new FakeCurrentUser { Id = "user123" })
    .WithFake<IModuleBus>(new RecordingModuleBus());

var client = factory.CreateClient();
var response = await client.PostAsync("/api/products", ...);
```

**Available fakes:**
- `FakeCurrentTenant` — Inject tenant context
- `FakeCurrentUser` — Inject user + roles
- `FakePermissionRegistry` — Mock authorization
- `FakeFeatureGate` — Feature flag toggles
- `FakeCacheService` — In-memory cache
- `RecordingModuleBus` — Capture published events

## Event Assertions

Test event-driven behavior with built-in helpers:

```csharp
[Fact]
public async Task CreateProduct_PublishesEvent()
{
    var bus = factory.Services.GetRequiredService<RecordingModuleBus>();
    var client = factory.CreateClient();
    
    await client.PostAsync("/api/products", 
        JsonContent.Create(new { name = "Widget" }));
    
    bus.HasPublished<ProductCreatedEvent>(
        e => e.Name == "Widget").Should().BeTrue();
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
