---
sidebar_position: 2
---

# Entity Framework Core

Modulus integrates deeply with EF Core for relational database access.

## ModuleDbContext

Every module uses `ModuleDbContext` as its base class:

```csharp
public sealed class CatalogDbContext : ModuleDbContext
{
    public DbSet<Product> Products => Set<Product>();

    public CatalogDbContext(
        DbContextOptions<CatalogDbContext> options,
        ICurrentTenant tenant,
        ICurrentUser user,
        DomainEventDispatcher dispatcher,
        IServiceProvider services,
        TimeProvider? clock = null)
        : base(options, tenant, user, dispatcher, services, clock) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(200);
        });
    }
}
```

## Built-in Features

`ModuleDbContext` provides:

| Feature | Description |
|---------|-------------|
| **Table prefixing** | All tables prefixed with `{Module}_` |
| **Audit stamping** | `CreatedAt`, `UpdatedAt` fields auto-filled |
| **Soft delete** | `IsDeleted`, `DeletedAt`, `DeletedBy` filters |
| **Multi-tenant isolation** | Query filter by `TenantId` |
| **PII encryption** | `[ProtectedPersonalData]` columns encrypted at rest |
| **Outbox enqueue** | Integration events enqueued in same transaction |

## Provider Configuration

The provider call goes *inside* the options lambda:

```csharp
// SQLite
services.AddModuleDatabase<CatalogDbContext>(options =>
    options.UseSqlite(config.GetConnectionString("Catalog")));

// SQL Server
services.AddModuleDatabase<CatalogDbContext>(options =>
    options.UseSqlServer(config.GetConnectionString("Catalog")));

// PostgreSQL
services.AddModuleDatabase<CatalogDbContext>(options =>
    options.UseNpgsql(config.GetConnectionString("Catalog")));

// MySQL
services.AddModuleDatabase<CatalogDbContext>(options =>
    options.UseMySql(config.GetConnectionString("Catalog"), ServerVersion.AutoDetect(...)));
```

Per-tenant databases resolve the connection string per scope (scoped options):

```csharp
services.AddModuleDatabase<CatalogDbContext>(
    sp => sp.GetRequiredService<ICurrentTenant>().ConnectionString ?? hostConnection,
    options => options.UseNpgsql(...));
```

## Entity Configuration

### Fluent API

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<Product>(e =>
    {
        e.HasKey(p => p.Id);
        e.Property(p => p.Name).HasMaxLength(200).IsRequired();

        e.HasIndex(p => p.Name);
    });
}
```

### Attributes

```csharp
public sealed class Product : AggregateRoot<Guid>
{
    [MaxLength(200)]
    [Required]
    public string Name { get; set; } = string.Empty;
}
```

## IUnitOfWork

Each module defines its own Unit of Work:

```csharp
public interface IUnitOfWork
{
    Task<int> CommitAsync(CancellationToken cancellationToken = default);
}
```

Bind it to the DbContext:

```csharp
services.AddScoped<IUnitOfWork>(sp =>
    sp.GetRequiredService<CatalogDbContext>());
```

Handlers use it to commit changes:

```csharp
public async Task<Guid> HandleAsync(CreateProductCommand command, CancellationToken ct)
{
    var product = new Product { Name = command.Name };
    await _repo.AddAsync(product, ct);
    await _unitOfWork.CommitAsync(ct);
    return product.Id;
}
```

## Design-Time Factory

For `dotnet ef` migrations without the full app (connection from
`{MODULE}_CONNECTION` env, else the design-time default; tenant/user/
dispatcher are no-op stubs):

```csharp
public sealed class CatalogDbContextFactory
    : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CATALOG_CONNECTION")
            ?? "Data Source=catalog.db";

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new CatalogDbContext(
            options,
            DesignTimeContext.Tenant,
            DesignTimeContext.User,
            DesignTimeContext.Dispatcher,
            DesignTimeContext.Services);
    }
}
```

## EntityContextMap

The framework routes entities to the correct DbContext via `IEntityContextMap`:

```csharp
// Registration-time mapping (no DB scan)
services.AddModuleDatabase<CatalogDbContext>(options =>
    options.UseSqlite(config.GetConnectionString("Catalog")));

// At runtime, EfRepository<Product> resolves only CatalogDbContext
// instead of scanning all registered contexts
```

This is built once as a singleton — no performance overhead at runtime.
