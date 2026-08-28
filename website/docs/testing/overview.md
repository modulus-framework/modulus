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
| `Modulus.Inbox.Tests` | Inbox deduplication |
| `Modulus.Outbox.Tests` | Outbox processor |
| `Modulus.Mediator.Tests` | Pipeline behaviors |
| `Modulus.Identity.Tests` | Password grant, external IdP validation |
| `Modulus.Testing.Tests` | Test harness |

## See Also

- [Integration Tests](integration-tests) — WebApplicationFactory harness
