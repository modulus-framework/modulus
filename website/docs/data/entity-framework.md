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

    public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.Price).HasPrecision(18, 2);
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

Each provider has an extension method:

```csharp
// SQLite
services.AddModuleDatabase<CatalogDbContext>(config)
    .UseSqlite(config.GetConnectionString("Catalog"));

// SQL Server
services.AddModuleDatabase<CatalogDbContext>(config)
    .UseSqlServer(config.GetConnectionString("Catalog"));

// PostgreSQL
services.AddModuleDatabase<CatalogDbContext>(config)
    .UseNpgsql(config.GetConnectionString("Catalog"));

// MySQL
services.AddModuleDatabase<CatalogDbContext>(config)
    .UseMySql(config.GetConnectionString("Catalog"), ServerVersion.AutoDetect(...));
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
        e.Property(p => p.Price).HasPrecision(18, 2);

        e.HasIndex(p => p.Name);
        e.HasOne(p => p.Category).WithMany(c => c.Products);
    });
}
```

### Attributes

```csharp
public sealed class Product : AggregateRoot<Guid>
{
    [MaxLength(200)]
    [Required]
    public string Name { get; private set; } = default!;

    [Precision(18, 2)]
    public decimal Price { get; private set; }
}
```

## IUnitOfWork

Each module defines its own Unit of Work:

```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

Bind it to the DbContext:

```csharp
services.AddScoped<IUnitOfWork>(sp =>
    sp.GetRequiredService<CatalogDbContext>());
```

Handlers use it to commit changes:

```csharp
public async Task<ProductDto> HandleAsync(CreateProduct command, CancellationToken ct)
{
    var product = new Product(command.Name, command.Price);
    _unitOfWork.Products.Add(product);
    await _unitOfWork.SaveChangesAsync(ct);
    return new ProductDto(product.Id, product.Name, product.Price);
}
```

## Design-Time Factory

For `dotnet ef` migrations without the full app:

```csharp
public sealed class CatalogDbContextFactory
    : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<CatalogDbContext>();
        optionsBuilder.UseSqlite("Data Source=catalog.db");
        return new CatalogDbContext(optionsBuilder.Options);
    }
}
```

## EntityContextMap

The framework routes entities to the correct DbContext via `IEntityContextMap`:

```csharp
// Registration-time mapping (no DB scan)
services.AddModuleDatabase<CatalogDbContext>(config);

// At runtime, EfRepository<Product> resolves only CatalogDbContext
// instead of scanning all registered contexts
```

This is built once as a singleton — no performance overhead at runtime.
