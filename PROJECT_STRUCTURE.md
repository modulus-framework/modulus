# FoodDeliveryApp — Project Structure

A food-delivery backend built as a **.NET 8 modular monolith** applying **Domain-Driven Design (DDD)**, **CQRS**, and **Clean Architecture**. Modules communicate asynchronously through an **Outbox/Inbox** mechanism, and cross-cutting concerns (auth, caching, logging, health) are shared via a reusable kernel.

---

## 1. Solution Layout

```
FoodDeliveryApp/
├── FoodDeliveryApp.sln              # Visual Studio solution (13 projects, solution folders)
├── Directory.Build.props            # Central build settings (net8.0, nullable, analyzers)
├── docker-compose.yml               # API + Postgres + Keycloak + Seq + Redis
├── docker-compose.override.yml      # Local dev overrides
├── docker-compose.dcproj            # Docker Compose project (VS tooling)
├── .dockerignore / .editorconfig / .gitignore
└── src/
    ├── API/                         # ASP.NET Core host (composition root)
    │   └── FoodDelivery.Api/
    ├── Modules/                     # Feature modules (one bounded context each)
    │   ├── Basket/                  # 4-layer Basket module
    │   └── Catalog/                 # 4-layer Catalog module
    └── Shared/                      # Shared kernel (4 reusable layers)
        ├── FoodDelivery.Shared.Domain
        ├── FoodDelivery.Shared.Application
        ├── FoodDelivery.Shared.Infrastructure
        └── FoodDelivery.Shared.Presentation
```

The solution file groups the projects into three top-level solution folders — **API**, **Modules**, and **Shared** — mirroring the `src/` layout.

### Architectural style
- **Modular monolith** — each module (Basket, Catalog) is an independent bounded context with its own `DbContext`, schema, and table set.
- **Clean Architecture per module** — every module is split into 4 projects: `Domain`, `Application`, `Infrastructure`, `Presentation`.
- **CQRS** — commands/queries and their handlers are mediated by **MediatR 12**, with pipeline behaviors for validation, logging, and exception handling.
- **REPR endpoints** — there are **no controllers**; each use case is an `IEndpoint` class (Request–Endpoint–Response pattern) auto-discovered via reflection.
- **Reliable messaging** — **MassTransit** in-memory bus combined with a **Quartz**-scheduled **Outbox/Inbox** persisted in each module's database.

---

## 2. Top-Level Files

| File | Purpose |
|---|---|
| `FoodDeliveryApp.sln` | Binds all 13 projects and the solution folders. |
| `Directory.Build.props` | Sets `TargetFramework=net8.0`, `ImplicitUsings`, `Nullable`, strict analysis (`AnalysisLevel=latest`, `AnalysisMode=All`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`), and adds `SonarAnalyzer.CSharp 9.24.0.89429` globally. |
| `docker-compose.yml` | Orchestrates 5 services: `fooddelivery.api`, `fooddelivery-database` (Postgres), `fooddelivery.identity` (Keycloak), `fooddelivery.seq` (Seq logs), `fooddelivery.redis` (Redis cache). |
| `docker-compose.override.yml` | Development-time overrides. |
| `.editorconfig` | Code-style rules enforced on build. |

### docker-compose service ports

| Service | Image | Host Port → Container |
|---|---|---|
| `fooddelivery.api` | built from `src/API/FoodDelivery.Api/Dockerfile` | `5000 → 8080`, `5001 → 8081` |
| `fooddelivery-database` | `postgres:latest` (db: `fooddelivery`) | `5432 → 5432` |
| `fooddelivery.identity` | `quay.io/keycloak/keycloak:latest` | `18080 → 8080` |
| `fooddelivery.seq` | `datalust/seq:latest` | `5341 → 5341`, `8081 → 80` |
| `fooddelivery.redis` | `redis:latest` | `6379 → 6379` |

---

## 3. API Layer — `src/API/FoodDelivery.Api/`

The single deployable host. It does not contain business logic; it composes the shared kernel and both modules.

```
FoodDelivery.Api/
├── Program.cs                              # Composition root (minimal hosting model)
├── appsettings.json / .Development.json    # Base + dev config (conn strings, auth, Serilog)
├── modules.basket.json(.Development)       # Basket module config (Outbox/Inbox: 5s interval, batch 50)
├── modules.Catalogs.json(.Development)     # Catalog module config (Outbox/Inbox: 5s interval, batch 50)
├── Dockerfile                              # Multi-stage build (aspnet:8.0 / sdk:8.0)
├── Properties/launchSettings.json
├── Extensions/
│   ├── ConfigurationExtensions.cs          # AddModuleConfiguration() loads modules.{name}.json
│   ├── MigrationExtensions.cs              # ApplyMigrations() runs EF Core migrations
│   ├── SwaggerExtensions.cs                # AddSwaggerDocumentation()
│   └── KeyCloakHealthChecksBuilderExtensions.cs
├── Middleware/
│   └── GlobalExceptionHandler.cs           # IExceptionHandler → RFC 7231 ProblemDetails
└── OpenTelemetry/
    └── DiagnosticsConfig.cs                # ServiceName = "FoodDeliveryApp"
```

### `Program.cs` pipeline
1. Serilog bootstrap from configuration.
2. `GlobalExceptionHandler` + ProblemDetails + Swagger docs.
3. `AddApplication(...)` for both module Application assemblies (MediatR + behaviors + validators).
4. Shared `AddInfrastructure(...)` (DB + Redis connections, empty consumers list).
5. Health Checks (NpgSql + Redis + KeyCloak URL group).
6. Module configs + `AddCatalogModule()` + `AddBasketModule()`.
7. Middleware order: Swagger (dev) → auto-migration (dev) → `/health` endpoint → Serilog request logging → exception handler → authentication → authorization → `MapEndpoints()` → run.

**Project references:** `FoodDelivery.Api.csproj` → both module Infrastructure projects transitively (via module composition), `Serilog.AspNetCore`, `Serilog.Sinks.Seq`, `Swashbuckle.AspNetCore`, `AspNetCore.HealthChecks.UI.Client`, `Microsoft.EntityFrameworkCore.Design/Tools`.

---

## 4. Shared Kernel — `src/Shared/`

Reusable building blocks consumed by every module. Also split into 4 layers following the same Clean Architecture discipline.

### 4.1 FoodDelivery.Shared.Domain

Foundational DDD primitives and shared value objects.

```
FoodDelivery.Shared.Domain/
├── AggregateRoot.cs        # AggregateRoot<TId> : Entity<TId>, IHasDomainEvents
├── Entity.cs               # Entity<TId> base (equality by Id + type)
├── EntityId.cs
├── IDomainEvent.cs / DomainEvent.cs / IHasDomainEvents.cs
├── Result.cs / Result<TValue>.cs   # Functional result monad
├── Error.cs / ErrorType.cs / ValidationError.cs
├── IRepository.cs / IDomainService.cs
├── IAuditable.cs / ISoftDeletable.cs / IHasPersonalData.cs / IHasRetentionPolicy.cs
├── PagedResult.cs / Specification.cs / ImmutableCollections.cs
├── Enums/
│   ├── CountryCode.cs
│   └── UserRole.cs
└── ValueObjects/
    ├── Address.cs / Email.cs / Money.cs / PhoneNumber.cs / Quantity.cs
    └── UserId.cs / CookId.cs / OrderId.cs
```

**Packages:** `MediatR.Contracts 2.0.1`, `Ulid 1.3.3`.

### 4.2 FoodDelivery.Shared.Application

CQRS contracts, MediatR wiring, and pipeline behaviors.

```
FoodDelivery.Shared.Application/
├── ApplicationConfiguration.cs     # AddApplication(): MediatR + 3 pipeline behaviors
├── Authorization/                  # IPermissionService, PermissionsResponse
├── Behaviors/
│   ├── ExceptionHandlingPipelineBehavior.cs
│   ├── RequestLoggingPipelineBehavior.cs
│   └── ValidationPipelineBehavior.cs
├── Caching/                        # ICacheService
├── Clock/                          # IDateTimeProvider
├── Data/                           # IDbConnectionFactory (Dapper)
├── EventBus/                       # IEventBus, IIntegrationEvent(Handler), IntegrationEvent base
├── Exceptions/                     # FoodDeliveryException
└── Messaging/                      # ICommand, ICommandHandler, IQuery, IQueryHandler,
                                    #   IDomainEventHandler (CQRS contracts)
```

**Packages:** `MediatR 12.2.0`, `FluentValidation.DependencyInjectionExtensions 11.9.1`, `Dapper 2.1.44`, `Serilog 3.1.1`, `Microsoft.Extensions.Logging.Abstractions 8.0.1`.
**References:** `Shared.Domain`.

### 4.3 FoodDelivery.Shared.Infrastructure

Concrete implementations of auth, caching, persistence helpers, and the Outbox/Inbox engine.

```
FoodDelivery.Shared.Infrastructure/
├── InfrastructureConfiguration.cs  # AddInfrastructure(): auth, caching, DB, MassTransit, Quartz
├── Authentication/                 # JwtBearer config, CustomClaims, ClaimsPrincipalExtensions
├── Authorization/                  # Permission policy provider, handler, requirement,
│                                   #   CustomClaimsTransformation (Scrutor)
├── Caching/                        # CacheOptions, CacheService (Redis + memory fallback)
├── Clock/                          # DateTimeProvider
├── Configuration/                  # GetValueOrThrow helpers
├── Conversions/                    # JsonConverter
├── Data/                           # DbConnectionFactory (Dapper), GenericArrayHandler
├── EventBus/                       # EventBus implementation
├── Inbox/                          # InboxMessage, consumer, config, IntegrationEventHandlersFactory
├── Outbox/                         # OutboxMessage, consumer, config, InsertOutboxMessagesInterceptor,
│                                   #   DomainEventHandlersFactory
└── Serialization/                  # SerializerSettings (Newtonsoft.Json)
```

**Packages:** `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.2`, `EFCore.NamingConventions 8.0.3`, `Microsoft.AspNetCore.Authentication.JwtBearer 8.0.4`, `Microsoft.Extensions.Caching.StackExchangeRedis 8.0.4`, `MassTransit 8.2.1`, `MassTransit.Redis 8.2.1`, `Quartz.Extensions.Hosting 3.8.1`, `Scrutor 4.2.2`, `Newtonsoft.Json 13.0.3`, plus three `AspNetCore.HealthChecks.*` packages.
**References:** `Shared.Application`.

### 4.4 FoodDelivery.Shared.Presentation

Minimal-API endpoint plumbing and result→HTTP mapping.

```
FoodDelivery.Shared.Presentation/
├── Endpoints/
│   ├── IEndpoint.cs                # Interface: void MapEndpoint(IEndpointRouteBuilder)
│   └── EndpointExtensions.cs       # AddEndpoints() / MapEndpoints() via reflection
└── Results/
    ├── ApiResults.cs
    └── ResultExtensions.cs         # Converts Result → HTTP responses
```

**References:** `FrameworkReference Microsoft.AspNetCore.App`, `Shared.Domain`.

---

## 5. Modules — `src/Modules/`

Each module is a self-contained bounded context following the **same 4-layer structure** as the shared kernel. The composition root for a module lives in its `*Module.cs` extension inside the Infrastructure project (e.g. `AddBasketModule()` / `AddCatalogModule()`).

```
Modules/<Name>/
└── FoodDelivery.Modules.<Name>/
    ├── Domain/           # Aggregates, entities, value objects, events, specs, repositories, errors
    ├── Application/      # Commands, queries, handlers, DTOs, validators, abstractions
    ├── Infrastructure/   # DbContext, EF configs, migrations, repositories, Outbox/Inbox jobs, DI
    └── Presentation/     # REPR endpoint classes (IEndpoint), permissions, Swagger tags
```

### 5.1 Basket Module

Manages a user's shopping basket against a specific cook, with expiration and retention rules.

#### 5.1.1 Basket.Domain
- **Entities:** `Basket` (aggregate root: tracks `UserId`, `CookId`, currency, items, expiration, retention; implements `IHasPersonalData`, `IHasRetentionPolicy`, `IAuditable`), `BasketItem`.
- **ValueObjects:** `BasketId`, `BasketConfiguration`, `BasketExpiration`, `ExtraOption`, `FoodItemId`.
- **Events:** `BasketCreatedEvent`, `BasketAbandonedEvent`, `BasketClearedEvent`, `BasketExpiredEvent`, `BasketItemAddedEvent`, `BasketItemQuantityUpdatedEvent`, `BasketItemRemovedEvent`.
- **Repositories:** `IBasketRepository`.
- **Services:** `BasketDomainService`.
- **Errors:** `BasketErrors`.
- **References:** `Shared.Domain`.

#### 5.1.2 Basket.Application
- **CQRS commands:** `AddItemToBasket`, `ClearBasket`, `CreateBasket`, `ExtendBasketExpiration`, `MarkBasketAsAbandoned`, `RemoveItemFromBasket`, `UpdateBasketItemQuantity` (each `Command` + `CommandHandler`).
- **CQRS queries:** `GetAbandonedBaskets`, `GetBasketById`, `GetBasketByUserId`, `GetBasketSummary`, `GetExpiredBaskets`, `ValidateBasketForCheckout`.
- **Dtos:** `BasketDto`, `BasketItemDto`, `BasketItemValidationDto`, `BasketSummaryDto`, `BasketValidationResultDto`, `ExtraOptionDto`.
- **Abstractions:** `IUserContext`, `IUnitOfWork`.
- **References:** `Shared.Application`, `Basket.Domain`.

#### 5.1.3 Basket.Infrastructure
- **Composition root:** `BasketModule.AddBasketModule()` — registers `BasketDbContext`, `IUnitOfWork`, `BasketDomainService`, `IBasketRepository`, `IUserContext`, Outbox/Inbox Quartz jobs, auto-discovers event handlers with idempotent Scrutor decoration.
- **Database:** `BasketDbContext`, `Schemas.cs` (`Basket`), `Migrations/` (InitialCreate + snapshot).
- **Persistence:** `BasketRepository`, `BasketConfiguration` (EF mapping).
- **Auth:** `UserContext` (impl of `IUserContext`).
- **Outbox/Inbox:** `ProcessOutboxJob`, `ProcessInboxJob`, `ConfigureProcessOutboxJob`, `ConfigureProcessInboxJob`, idempotent handler decorators, `IntegrationEventConsumer`, `OutboxOptions`/`InboxOptions`.
- **References:** `Shared.Infrastructure`, `Basket.Application`, `Basket.Domain`, `Basket.Presentation`.

#### 5.1.4 Basket.Presentation
- **Endpoints (one REPR class per use case):** `AddItemToBasket`, `ClearBasket`, `CreateBasket`, `ExtendBasketExpiration`, `GetAbandonedBaskets`, `GetBasketById`, `GetBasketByUserId`, `GetBasketSummary`, `GetExpiredBaskets`, `MarkBasketAsAbandoned`, `RemoveItemFromBasket`, `UpdateBasketItemQuantity`, `ValidateBasketForCheckout`.
- **Support:** `Permissions.cs`, `Tags.cs`, `AssemblyReference.cs`.
- **References:** `Shared.Presentation`, `Basket.Application`.

### 5.2 Catalog Module

Manages the product catalog: categories (with hierarchy) and food items (with inventory, pricing, dietary preferences, allergens, media, ratings).

#### 5.2.1 Catalog.Domain
- **Entities:** `FoodItem` (aggregate root with rich invariants), `Category`, `ExtraOption`, `InventoryTransaction`.
- **ValueObjects:** `CategoryId`, `ExtraOptionId`, `FoodItemId`, `InventoryId`, `InventoryQuantity`, `InventoryTransactionId`, `Rating`, `Slug`.
- **Enums:** `CategoryStatus`, `DietaryPreference`, `FoodItemStatus`, `InventoryOperationType`.
- **Events (incl. 2 integration events):** `Category*Event` (Created/Updated/Activated/Deactivated/Archived/FeaturedStatusChanged), `FoodItem*Event` (Created/Updated/Published/Unpublished/Discontinued/PriceChanged/CategoryChanged/FeaturedStatusChanged/RatingUpdated/StockAdded/StockRemoved/BackInStock/OutOfStock), `FoodItemPublishedIntegrationEvent`, `FoodItemOutOfStockIntegrationEvent`.
- **Specifications:** `ActiveCategorySpecification`, `AvailableFoodItemSpecification`, `FeaturedFoodItemSpecification`, `LowStockFoodItemSpecification`, `OutOfStockFoodItemSpecification`, `RootCategorySpecification`, `SubcategorySpecification`.
- **Repositories:** `ICategoryRepository`, `IFoodItemRepository`.
- **Services:** `CategoryDomainService`, `FoodItemDomainService`.
- **Errors:** `CatalogErrors`.
- **References:** `Shared.Domain`.

#### 5.2.2 Catalog.Application
- **Categories commands:** `ActivateCategory`, `AddCategoryMetadata`, `ArchiveCategory`, `CreateRootCategory`, `CreateSubcategory`, `DeactivateCategory`, `DeleteCategory`, `MoveCategory`, `SetCategoryFeatured`, `UpdateCategory`, `UpdateCategoryMedia`.
- **Categories queries:** `GetActiveCategories`, `GetCategoryById`, `GetCategoryBySlug`, `GetCategoryHierarchy`, `GetCategoryTree`, `GetFeaturedCategories`, `GetRootCategories`, `GetSubcategories`.
- **FoodItems commands:** `CreateFoodItem`, `UpdateFoodItemBasicInfo/Price/Category/Rating`, `PublishFoodItem`, `UnpublishFoodItem`, `DiscontinueFoodItem`, `SetFoodItemFeatured`, `Add/RemoveImage`, `Add/RemoveAllergen`, `Add/RemoveDietaryPreference`, `Add/Remove/UpdateExtraOption`, `Add/Remove/Reserve/ReleaseFoodItemStock`.
- **FoodItems queries:** `GetAvailableFoodItems`, `GetFeaturedFoodItems`, `GetFoodItemById`, `GetFoodItemBySlug`, `GetFoodItemsByCategory`, `GetFoodItemsByCook`, `GetFoodItemsByDietaryPreference`, `GetLowStockFoodItems`, `GetOutOfStockFoodItems`, `SearchFoodItems`, `GetFoodItemInventoryHistory`.
- **Validators:** FluentValidation validators on several commands/queries.
- **Dtos:** `CategoryDto`, `CategoryHierarchyDto`, `FoodItemDto`, `FoodItemDetailDto`, `ExtraOptionDto`, `InventoryTransactionDto`.
- **Abstractions:** `ICookContext`, `IUnitOfWork`.
- **References:** `Shared.Application`, `Catalog.Domain`.

#### 5.2.3 Catalog.Infrastructure
- **Composition root:** `CatalogModule.AddCatalogModule()` — same pattern as Basket (DbContext with snake_case naming + outbox interceptor, UoW, repositories, domain services, `ICookContext`, Quartz Outbox/Inbox, idempotent handlers, endpoint discovery).
- **Database:** `CatalogDbContext`, `Schemas.cs` (`Catalog`), `Migrations/` (InitialCreate + snapshot).
- **Persistence:** `FoodItemRepository`, `CategoryRepository`, `FoodItemConfiguration`, `CategoryConfiguration`.
- **Auth:** `CookContext` (impl of `ICookContext`).
- **Outbox/Inbox:** identical set to Basket.
- **References:** `Shared.Infrastructure`, `Catalog.Application`, `Catalog.Presentation`.

#### 5.2.4 Catalog.Presentation
- **Categories endpoints:** `ActivateCategory`, `ArchiveCategory`, `CreateRootCategory`, `CreateSubcategory`, `DeactivateCategory`, `DeleteCategory`, `GetCategoryById`, `GetCategoryBySlug`, `GetFeaturedCategories`, `GetRootCategories`, `GetSubcategories`, `MoveCategory`, `SetCategoryFeatured`, `UpdateCategory`, `UpdateCategoryMedia`.
- **FoodItems endpoints:** `CreateFoodItem`, `UpdateFoodItemBasicInfo/Price/Category/Rating`, `Publish/Unpublish/DiscontinueFoodItem`, `SetFoodItemFeatured`, `Add/RemoveFoodItemImage`, `Add/RemoveAllergen`, `Add/Remove/UpdateExtraOption`, `Add/Remove/Reserve/ReleaseFoodItemStock`, `GetAvailable/FeaturedFoodItems`, `GetFoodItemById/BySlug`, `GetFoodItemsByCategory/ByCook/ByDietaryPreference`, `GetLowStock/OutOfStockFoodItems`, `SearchFoodItems`, `GetFoodItemInventoryHistory`.
- **Support:** `Permissions.cs`, `Tags.cs`, `AssemblyReference.cs`.
- **References:** `Shared.Presentation`, `Catalog.Application`.

---

## 6. Project Reference Graph

Dependencies flow strictly **inward** toward the domain. Infrastructure depends on Presentation only so the module composition root can register endpoints.

```
Shared.Domain  ◄── Shared.Application  ◄── Shared.Infrastructure
                    ▲                            ▲
                    │                            │
Modules.<X>.Domain ◄── Modules.<X>.Application ◄── Modules.<X>.Infrastructure ──► Modules.<X>.Presentation
        ▲                                              ▲                              │
        │                                              │                              │
   Shared.Domain                              Shared.Infrastructure            Shared.Presentation
                                              Shared.Application
```

`FoodDelivery.Api` references the module Infrastructure projects (transitively pulling in everything) and is the only project that produces an executable.

---

## 7. Dockerfile — `src/API/FoodDelivery.Api/Dockerfile`

Multi-stage build:
1. **base** — `mcr.microsoft.com/dotnet/aspnet:8.0`, WORKDIR `/app`, exposes `8080/8081`.
2. **build** — `mcr.microsoft.com/dotnet/sdk:8.0`; copies all 13 `.csproj` files, `dotnet restore`, `dotnet build -c Release` → `/app/build`.
3. **publish** — `dotnet publish -c Release /p:UseAppHost=false` → `/app/publish`.
4. **final** — copies publish output; entrypoint `dotnet /app/FoodDelivery.Api.dll`.

---

## 8. Technology Stack

| Concern | Choice |
|---|---|
| Runtime / framework | .NET 8 / ASP.NET Core (minimal hosting) |
| ORM | EF Core 8 (`Npgsql.EntityFrameworkCore.PostgreSQL`) + Dapper (read sides) |
| Database | PostgreSQL (snake_case naming via `EFCore.NamingConventions`) |
| Cache | Redis (`Microsoft.Extensions.Caching.StackExchangeRedis`) with in-memory fallback |
| CQRS / mediation | MediatR 12 with FluentValidation pipeline behaviors |
| Messaging | MassTransit 8 (in-memory bus) + per-module **Outbox/Inbox** processed by **Quartz** |
| Authentication | Keycloak via JWT/OIDC (`Microsoft.AspNetCore.Authentication.JwtBearer`) |
| Authorization | Custom permission-based provider/handler with `Scrutor` decoration |
| Logging | Serilog (Console + Seq sinks) |
| Health checks | `AspNetCore.HealthChecks.*` (NpgSql, Redis, Keycloak URL group) + UI |
| API docs | Swashbuckle / Swagger |
| Identifiers | `Ulid` for strongly-typed IDs |
| Static analysis | `SonarAnalyzer.CSharp`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true` |

---

## 9. Conventions

- **Naming:** projects use `FoodDelivery.<Area>.<Layer>`; namespaces match (Catalog csproj sets an explicit `RootNamespace`).
- **Endpoints:** each use case is a single class implementing `IEndpoint.MapEndpoint(...)` — no controllers, no `Controllers/` folder.
- **Module composition:** every module exposes an `Add<Name>Module()` extension in its Infrastructure project; the API host calls them in `Program.cs`.
- **Config files:** each module ships a `modules.<name>.json` (and `.Development.json`) loaded via `AddModuleConfiguration()`.
- **DB schemas:** each module owns its own schema constant (`Schemas.Basket`, `Schemas.Catalog`) and migrations.
- **Cross-module integration:** domain events are raised on aggregates, persisted to the module Outbox by `InsertOutboxMessagesInterceptor`, forwarded as integration events, and consumed by other modules' Inbox with idempotent handler decorators.

---

## 10. Known Inconsistencies

- Swagger title is still **"Evently API"** in `SwaggerExtensions.cs` (leftover from a previous template).
- Config keys use the legacy plural **`Catalogs:`** section while the project namespace is `Catalog` (e.g. `modules.Catalogs.json`, `FoodDelivery.Modules.Catalogs.Infrastructure` in `appsettings.Development.json`).
- Some build artifacts/obj caches reference older module names (`Auth`, `Cart`, `Users`), indicating the solution was refactored/renamed.
