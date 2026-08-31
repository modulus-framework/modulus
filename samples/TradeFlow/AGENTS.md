# AGENTS.md — TradeFlow Sample

Guidance for AI agents (and humans) working on this sample app.

## Project

TradeFlow is a modular-monolith **.NET 10** ERP sample built on the **Modulus framework** (`Cobytelabs.Modulus.*` packages). It demonstrates the 4-layer module pattern: Domain → Application → Infrastructure → Presentation, plus IntegrationEvents and PublicApi contract projects.

## Prerequisites

- .NET SDK **10.0.109** or newer
- Docker (for Keycloak, PostgreSQL, Redis, RabbitMQ, Seq, MinIO)

## Common commands

All commands are run from this directory (`samples/TradeFlow`).

| Task | Command |
|------|---------|
| Restore + build | `dotnet build TradeFlow.slnx` |
| Run unit tests | `dotnet test TradeFlow.slnx --filter "Category=Unit"` |
| Run E2E tests | `dotnet test TradeFlow.slnx --filter "Category=Integration"` |
| Start infrastructure | `docker compose up -d` |
| Run API | `dotnet run --project src/API/TradeFlow.Api/TradeFlow.Api.csproj` |
| Run with migrations | `dotnet run --project src/API/TradeFlow.Api/TradeFlow.Api.csproj -- --migrate` |
| Run with seeding | `dotnet run --project src/API/TradeFlow.Api/TradeFlow.Api.csproj -- --seed` |

## Module layout (Vendors = exemplar)

Each module follows the 6-project shape:

```
src/Modules/{Module}/
  TradeFlow.Modules.{Module}.Domain/
    Entities/          # aggregate roots + entities
    ValueObjects/      # typed IDs, immutable VOs
    Enums/
    Errors/            # {Module}Errors — Error factory per module
    Events/            # domain events (records)
    Repositories/      # I*Repository interfaces ONLY
  TradeFlow.Modules.{Module}.Application/
    Abstractions/      # IUnitOfWork, port interfaces
    Vendors/
      Commands/{UseCase}/
        {UseCase}Command.cs
        {UseCase}CommandHandler.cs
        {UseCase}CommandValidator.cs
      Queries/{UseCase}/
        {UseCase}Query.cs
        {UseCase}QueryHandler.cs
      Dtos/
    DomainEventHandlers/
  TradeFlow.Modules.{Module}.IntegrationEvents/
    # Published event contracts (no logic)
  TradeFlow.Modules.{Module}.PublicApi/
    # Sync cross-module interfaces (no logic)
  TradeFlow.Modules.{Module}.Infrastructure/
    {Module}Module.cs    # DI composition root
    Database/            # DbContext, design-time factory, migrations
    Repositories/
    PublicApi/           # implements PublicApi interfaces
  TradeFlow.Modules.{Module}.Presentation/
    Permissions.cs       # permission constants
    Tags.cs              # OpenAPI tags
    {Feature}/
      {Endpoint}.cs      # one endpoint = one file
```

## Test layout

```
tests/
  Modules/{Module}/TradeFlow.Modules.{Module}.UnitTests/
  E2E/TradeFlow.E2ETests/          # full-host HTTP smoke tests
```

## Key conventions

- **Per-module DbContext** — each module owns its own `{Module}DbContext` + connection string.
- **Per-module IUnitOfWork** — each module defines its own `IUnitOfWork` in `Application/Abstractions/`.
- **Integration events** — published facts live in a separate `IntegrationEvents` project.
- **PublicApi** — sync cross-module interfaces live in a separate `PublicApi` project.
- **One endpoint = one file** — co-located request/response records in the same file.
- **Use-case folders** — each command/query gets its own folder with Command/Handler/Validator trio.
- **Errors factory** — one `{Module}Errors` static class per module in `Domain/Errors/`.
- **TreatWarningsAsErrors** — enabled globally; any new warning fails the build.
