# AGENTS.md — ProcureFlow Sample

Guidance for AI agents (and humans) working on this sample app.

## Project

ProcureFlow is a modular-monolith **.NET 10** ERP sample built on the **Modulus framework** (`Cobytelabs.Modulus.*` packages). It demonstrates the 4-layer module pattern: Domain → Application → Infrastructure → Presentation, plus IntegrationEvents and PublicApi contract projects.

## Prerequisites

- .NET SDK **10.0.109** or newer
- Docker (for Keycloak, PostgreSQL, Redis, RabbitMQ, Seq, MinIO)

## Common commands

All commands are run from this directory (`samples/ProcureFlow`).

| Task | Command |
|------|---------|
| Restore + build | `dotnet build ProcureFlow.slnx` |
| Run unit tests | `dotnet test ProcureFlow.slnx --filter "Category=Unit"` |
| Run E2E tests | `dotnet test ProcureFlow.slnx --filter "Category=Integration"` |
| Start infrastructure | `docker compose up -d` |
| Run API | `dotnet run --project src/API/ProcureFlow.Api/ProcureFlow.Api.csproj` |
| Run with migrations | `dotnet run --project src/API/ProcureFlow.Api/ProcureFlow.Api.csproj -- --migrate` |
| Run with seeding | `dotnet run --project src/API/ProcureFlow.Api/ProcureFlow.Api.csproj -- --seed` |

## Module layout (Vendors = exemplar)

Each module follows the 6-project shape:

```
src/Modules/{Module}/
  ProcureFlow.Modules.{Module}.Domain/
    Entities/          # aggregate roots + entities
    ValueObjects/      # typed IDs, immutable VOs
    Enums/
    Errors/            # {Module}Errors — Error factory per module
    Events/            # domain events (records)
    Repositories/      # I*Repository interfaces ONLY
  ProcureFlow.Modules.{Module}.Application/
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
  ProcureFlow.Modules.{Module}.IntegrationEvents/
    # Published event contracts (no logic)
  ProcureFlow.Modules.{Module}.PublicApi/
    # Sync cross-module interfaces (no logic)
  ProcureFlow.Modules.{Module}.Infrastructure/
    {Module}Module.cs    # DI composition root
    Database/            # DbContext, design-time factory, migrations
    Repositories/
    PublicApi/           # implements PublicApi interfaces
  ProcureFlow.Modules.{Module}.Presentation/
    Permissions.cs       # permission constants
    Tags.cs              # OpenAPI tags
    {Feature}/
      {Endpoint}.cs      # one endpoint = one file
```

## Test layout

```
tests/
  Modules/{Module}/ProcureFlow.Modules.{Module}.UnitTests/
  E2E/ProcureFlow.E2ETests/          # full-host HTTP smoke tests
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
