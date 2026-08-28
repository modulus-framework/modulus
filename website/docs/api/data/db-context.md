---
sidebar_position: 6
---

# DbContext API

## ModuleDbContext

```csharp
public abstract class ModuleDbContext : DbContext
{
    protected string TablePrefix { get; } = "";
    protected ICurrentTenant? CurrentTenant { get; }
    protected ICurrentUser? CurrentUser { get; }
    protected IOutboxWriter? OutboxWriter { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 1. Apply entity configurations
        // 2. Apply table prefixing
        // 3. Apply soft-delete query filter
        // 4. Apply tenant query filter
        // 5. Apply PII encryption
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // 1. Enqueue integration events to outbox
        // 2. Stamp audit fields (CreatedAt, UpdatedAt)
        // 3. Handle soft-delete conversions
        // 4. Call base.SaveChangesAsync()
    }
}
```

## AddModuleDatabase\<TContext\>

```csharp
public static IServiceCollection AddModuleDatabase<TContext>(
    this IServiceCollection services,
    IConfiguration config)
    where TContext : ModuleDbContext
{
    // Registers DbContext with provider-specific configuration
    // Registers generic IRepository<T>
    // Exposes context as DbContext for TransactionBehavior
}
```

## MigrateModulusDatabasesAsync

```csharp
public static async Task MigrateModulusDatabasesAsync(
    this IServiceProvider serviceProvider,
    DatabaseInitializationMode mode = DatabaseInitializationMode.MigrateOrCreate,
    CancellationToken ct = default)
{
    // Resolves every DbContext
    // Per module: Migrate() when migrations exist, else EnsureCreated()
}
```

## See Also

- [Entity Framework](/docs/data/entity-framework) — EF Core integration
