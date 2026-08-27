# Phase 1 Business Modules - Architecture Reference

## Quick Navigation

Each Phase 1 module follows the same 4-layer Clean Architecture pattern:

```
Module/
├── Domain/              # Business rules, aggregates, domain events
├── Application/         # CQRS commands/queries, DTOs, abstractions
├── Infrastructure/      # Database, handlers, dependency injection
└── Presentation/        # Minimal API endpoints
```

---

## Domain Layer Pattern

### Entity Structure (Example: Product)
```csharp
public sealed class Product : AggregateRoot<Guid>
{
    public string Name { get; private set; }
    [Classified(FieldClassification.Confidential)]
    public decimal UnitCost { get; private set; }
    
    private Product() { } // EF Core ctor
    
    // Factory method pattern
    public static Result<Product> Create(
        Guid id, string name, decimal unitCost, decimal listPrice,
        Guid tenantId, string createdBy)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Product>(Error.Validation(...));
        
        var product = new Product { Id = id, Name = name, ... };
        return Result.Success(product);
    }
}
```

**Key Patterns:**
- Private constructor + factory method (Result<T> return)
- Invariant validation at aggregate root level
- `[Classified]` attributes for sensitive fields
- `IHasOwner` / `IHasOrgUnit` interfaces for scope

---

## Application Layer Pattern

### Commands (Example: CreatePartnerCommand)
```csharp
public sealed record CreatePartnerCommand(
    string Name,
    string Type,
    string Email,
    string Phone,
    string Address) : ICommand<Result<Guid>>;
```

### Queries (Example: GetPartnerByIdQuery)
```csharp
public sealed record GetPartnerByIdQuery(Guid Id) : IQuery<PartnerDto?>;

public sealed record ListPartnersQuery(int Page, int PageSize) 
    : IQuery<PagedResult<PartnerDto>>;
```

### DTOs (Example: PartnerDto)
```csharp
public sealed record PartnerDto(
    Guid Id,
    string Name,
    string Type,
    string Email,
    string Phone,
    string Address,
    Guid OwnerId,
    Guid TenantId,
    bool IsActive);
```

**Key Patterns:**
- Records for immutability
- ICommand<Result<T>> for commands (return business results)
- IQuery<T> for queries (return domain data)
- Sealed types for security and optimization

---

## Infrastructure Layer Pattern

### DbContext (Example: PartnersDbContext)
```csharp
public sealed class PartnersDbContext(
    DbContextOptions<PartnersDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider serviceProvider)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, serviceProvider)
{
    public const string SchemaName = "partners";
    
    public DbSet<Partner> Partners => Set<Partner>();
    
    protected override string TablePrefix => string.Empty;
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);
    }
}
```

### DbContextFactory (Example: PartnersDbContextFactory)
```csharp
public sealed class PartnersDbContextFactory : IDesignTimeDbContextFactory<PartnersDbContext>
{
    public PartnersDbContext CreateDbContext(string[] args)
    {
        var connectionString = "Host=localhost;Database=modulus_sample_partners;...";
        var optionsBuilder = new DbContextOptionsBuilder<PartnersDbContext>()
            .UseNpgsql(connectionString);

        return new PartnersDbContext(
            optionsBuilder.Options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
```

### Command/Query Handlers (Example: CreatePartnerCommandHandler)
```csharp
internal sealed class CreatePartnerCommandHandler 
    : ICommandHandler<CreatePartnerCommand, Result<Guid>>
{
    private readonly PartnersDbContext _dbContext;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentUser _currentUser;

    public CreatePartnerCommandHandler(
        PartnersDbContext dbContext,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentTenant = currentTenant;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> HandleAsync(
        CreatePartnerCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _currentTenant.TenantId ?? Guid.Empty;
        var userId = _currentUser.UserId ?? Guid.Empty;

        var result = Partner.Create(
            Guid.NewGuid(),
            request.Name,
            request.Type,
            request.Email,
            request.Phone,
            request.Address,
            userId,
            tenantId);

        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        _dbContext.Partners.Add(result.Value);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(result.Value.Id);
    }
}
```

### Module Registration (Example: PartnersModule)
```csharp
public sealed class PartnersModule : ModulusModule
{
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Database connection string not configured");

        services.AddDbContext<PartnersDbContext>(options =>
            options.UseNpgsql(connectionString));
    }
}
```

### Seeding (Example: PartnersDbContextSeed)
```csharp
public static class PartnersDbContextSeed
{
    public static async Task SeedAsync(
        PartnersDbContext context,
        ILogger logger,
        Guid tenantId,
        Guid ownerId)
    {
        try
        {
            if (await context.Partners.AnyAsync())
                return;

            var partners = new[]
            {
                Partner.Create(Guid.NewGuid(), "Acme Corp", "Customer", ...).Value,
                Partner.Create(Guid.NewGuid(), "Tech Supplies Ltd", "Supplier", ...).Value,
                // ...
            };

            context.Partners.AddRange(partners);
            await context.SaveChangesAsync();

            logger.LogInformation("Partners module seeding completed: {PartnerCount} partners added", 
                partners.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Partners module");
            throw;
        }
    }
}
```

**Key Patterns:**
- Primary constructor for dependency injection
- Inherits from `ModuleDbContext` (gets framework services)
- `HandleAsync` method (not `Handle`) for mediator pattern
- Result<T> pattern for business logic results
- ICurrentTenant/ICurrentUser for multi-tenancy context

---

## Presentation Layer Pattern

### Endpoints (Example: PartnersEndpoints)
```csharp
public static class PartnersEndpoints
{
    public static void MapPartnersEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/partners")
            .WithName("Partners")
            .WithOpenApi();

        group.MapPost("/", CreatePartner)
            .WithName("CreatePartner")
            .WithOpenApi();

        group.MapGet("/{id}", GetPartnerById)
            .WithName("GetPartnerById")
            .WithOpenApi();

        group.MapGet("/", ListPartners)
            .WithName("ListPartners")
            .WithOpenApi();
    }

    private static async Task<IResult> CreatePartner(
        HttpContext context,
        IMediator mediator,
        CreatePartnerRequest request)
    {
        var command = new CreatePartnerCommand(
            request.Name, request.Type, request.Email,
            request.Phone, request.Address);

        var result = await mediator.SendAsync(command);

        return result.IsSuccess
            ? Results.Created($"/api/partners/{result.Value}", new { id = result.Value })
            : Results.BadRequest(new { error = result.Error?.Message });
    }

    private static async Task<IResult> GetPartnerById(
        IMediator mediator, Guid id)
    {
        var result = await mediator.QueryAsync(new GetPartnerByIdQuery(id));
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    private static async Task<IResult> ListPartners(
        IMediator mediator, int page = 1, int pageSize = 10)
    {
        var result = await mediator.QueryAsync(new ListPartnersQuery(page, pageSize));
        return Results.Ok(result);
    }
}

public sealed record CreatePartnerRequest(
    string Name, string Type, string Email, string Phone, string Address);
```

**Key Patterns:**
- MapGroup for REPR pattern (Representation-Oriented)
- `SendAsync` for commands, `QueryAsync` for queries
- IResult returns for structured responses
- Minimal method signatures (framework injects from DI + route/query params)

---

## Dependency Registration Flow

### 1. ModulusSampleHostModule (src/API/ModulusSample.Api/Modules/)
```csharp
[DependsOn(
    typeof(CatalogModule),
    typeof(PartnersModule),
    typeof(InventoryModule),
    typeof(SalesModule),
    // ... platform modules
)]
public sealed class ModulusSampleHostModule : ModulusModule
{
    // DependsOn drives automatic module discovery
}
```

### 2. Program.cs Configuration
```csharp
// Module configuration files
builder.Configuration.AddModuleConfiguration([
    "catalog", "partners", "inventory", "sales", ...
]);

// Application layer (handlers)
Assembly[] moduleApplicationAssemblies = [
    ModulusSample.Modules.Catalog.Application.AssemblyReference.Assembly,
    ModulusSample.Modules.Partners.Application.AssemblyReference.Assembly,
    // ...
];
builder.Services.AddApplication(moduleApplicationAssemblies);

// Endpoints
Assembly[] endpointAssemblies = [
    ModulusSample.Modules.Catalog.Presentation.AssemblyReference.Assembly,
    ModulusSample.Modules.Partners.Presentation.AssemblyReference.Assembly,
    // ...
];
app.MapModulusEndpoints(endpointAssemblies);

// Manual endpoint mapping for Phase 1
app.MapCatalogEndpoints();
app.MapPartnersEndpoints();
app.MapInventoryEndpoints();
app.MapSalesEndpoints();
```

### 3. Module Database Seeding
```csharp
// MigrationExtensions.ApplySeeding()
var tenantId = Guid.Empty; // Default tenant for demo
var ownerId = Guid.NewGuid(); // Placeholder for owner
var orgUnitId = Guid.NewGuid(); // Placeholder for org unit

await CatalogDbContextSeed.SeedAsync(catalogContext, logger, tenantId);
await PartnersDbContextSeed.SeedAsync(partnersContext, logger, tenantId, ownerId);
await InventoryDbContextSeed.SeedAsync(inventoryContext, logger, tenantId, orgUnitId);
await SalesDbContextSeed.SeedAsync(salesContext, logger, tenantId, orgUnitId);
```

---

## Mediator Method Reference

| Method | Use | Example |
|--------|-----|---------|
| `SendAsync<TRequest>(TRequest)` | Commands | `await mediator.SendAsync(new CreateProductCommand(...))` |
| `QueryAsync<TQuery, TResult>(TQuery)` | Queries | `await mediator.QueryAsync(new GetProductByIdQuery(id))` |

**Note:** Commands return `Result<T>` or `Result`; Queries return `T?` or `PagedResult<T>`.

---

## Database Schema Isolation

Each module gets its own Postgres schema for complete data isolation:

```sql
-- Module schemas
CREATE SCHEMA catalog;
CREATE SCHEMA partners;
CREATE SCHEMA inventory;
CREATE SCHEMA sales;

-- Tables live in their module schemas
catalog.products
partners.partners
inventory.warehouses
inventory.stocks
sales.sales_orders
```

This enables:
- ✅ Namespace isolation (no naming conflicts)
- ✅ Per-module backups/restores
- ✅ Simpler data scope queries (filter by schema)
- ✅ Future module extraction to separate databases

---

## Framework Integrations Per Module

| Feature | Module | Integration |
|---------|--------|------------|
| Field-level security | Catalog | `[Classified(Confidential)]` on Product.UnitCost/Margin |
| Data scope | Partners | `IHasOwner` interface + OwnerId property |
| Org scope | Inventory | `IHasOrgUnit` interface + OrgUnitId property |
| Org scope | Sales | `IHasOrgUnit` interface + OrgUnitId property |
| Multi-tenancy | All | `ICurrentTenant` in handlers + TenantId storage |
| Composition | Sales | `OrderLine` as EF Core OwnedMany entities |

---

**Last Updated:** 2026-08-11  
**Pattern Consistency:** All 4 Phase 1 modules follow identical architectural patterns for coherent team onboarding.
