namespace Modulus.AI.Connector.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Ai;
using Modulus.Core.Abstractions.Compliance;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.EntityFrameworkCore.ModelBuilding;
using Modulus.EntityFrameworkCore.Saving;
using Modulus.Events;
using Modulus.MultiTenancy;

[AiIndexed("Shop.Product")]
public sealed class Product : IHasTenantId, ISoftDelete
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public string Name { get; set; } = "";

    public string? Category { get; set; }

    public int Quantity { get; set; }

    public decimal Price { get; set; }

    public double Weight { get; set; }

    [Classified(FieldClassification.Confidential)]
    public decimal Cost { get; set; }

    [SecretData]
    public string? Token { get; set; }

    [PersonalInformation]
    public string? Buyer { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }
}

/// <summary>A store-generated key: cannot be journaled.</summary>
[AiIndexed("Shop.Counter")]
public sealed class Counter
{
    public int Id { get; set; }

    public int Value { get; set; }
}

/// <summary>Not indexed: never journaled.</summary>
public sealed class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Text { get; set; } = "";
}

[AiIndexed("Sales.Order")]
public sealed class Order
{
    public string Id { get; set; } = "";

    public decimal Total { get; set; }
}

public sealed class ShopDbContext(
    DbContextOptions<ShopDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
{
    protected override string TablePrefix => "shop_";

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Counter> Counters => Set<Counter>();

    public DbSet<Note> Notes => Set<Note>();
}

public sealed class SalesDbContext(
    DbContextOptions<SalesDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
{
    protected override string TablePrefix => "sales_";

    public DbSet<Order> Orders => Set<Order>();
}

/// <summary>A clock the tests move by hand.</summary>
public sealed class ManualTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Two module contexts on private in-memory SQLite databases, with the journal mapped into both.</summary>
public sealed class JournalTestHost : IAsyncDisposable
{
    public static readonly TenantInfo CompanyA = new(Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a"), "a");
    public static readonly TenantInfo CompanyB = new(Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b"), "b");

    private readonly SqliteConnection _shop;
    private readonly SqliteConnection _sales;
    private readonly ServiceProvider _root;

    public JournalTestHost(Action<IServiceCollection>? configure = null)
    {
        var name = Guid.NewGuid().ToString("N");
        _shop = new SqliteConnection($"DataSource=shop-{name};Mode=Memory;Cache=Shared");
        _sales = new SqliteConnection($"DataSource=sales-{name};Mode=Memory;Cache=Shared");
        _shop.Open();
        _sales.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<ICurrentTenant>(Tenant);
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddSingleton<IModuleModelContributor, AiChangeModelContributor>();
        services.AddSingleton<IModuleSaveContributor>(new AiChangeSaveContributor(Time));
        services.AddModuleDatabase<ShopDbContext>(o => o.UseSqlite(_shop));
        services.AddModuleDatabase<SalesDbContext>(o => o.UseSqlite(_sales));
        services.AddScoped<EfAiChangeFeed>();
        services.AddScoped<EfAiEntitySource>();
        configure?.Invoke(services);
        _root = services.BuildServiceProvider();

        using var scope = _root.CreateScope();
        using var host = Tenant.Change(null);
        scope.ServiceProvider.GetRequiredService<ShopDbContext>().Database.EnsureCreated();
        scope.ServiceProvider.GetRequiredService<SalesDbContext>().Database.EnsureCreated();
    }

    public ManualTime Time { get; } = new();

    public CurrentTenant Tenant { get; } = new();

    public IServiceProvider Services => _root;

    /// <summary>Runs <paramref name="work"/> in a fresh scope inside <paramref name="tenant"/>.</summary>
    public async Task<T> InAsync<T>(TenantInfo? tenant, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = _root.CreateAsyncScope();
        using var company = Tenant.Change(tenant);
        return await work(scope.ServiceProvider);
    }

    public Task InAsync(TenantInfo? tenant, Func<IServiceProvider, Task> work)
        => InAsync<bool>(tenant, async sp =>
        {
            await work(sp);
            return true;
        });

    /// <summary>Every journal row of both contexts (host context), oldest first.</summary>
    public Task<List<AiChangeRecord>> JournalAsync()
        => InAsync(null, async sp =>
        {
            var rows = await sp.GetRequiredService<ShopDbContext>().Set<AiChangeRecord>().AsNoTracking().ToListAsync();
            rows.AddRange(await sp.GetRequiredService<SalesDbContext>().Set<AiChangeRecord>().AsNoTracking().ToListAsync());
            return rows.OrderBy(r => r.OccurredAt).ThenBy(r => r.Sequence).ToList();
        });

    internal AiChangeJournalPurgeService Purger(TimeSpan retention)
        => new(
            _root.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AiChangeJournalOptions { Retention = retention }),
            Time,
            NullLogger<AiChangeJournalPurgeService>.Instance);

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        await _shop.DisposeAsync();
        await _sales.DisposeAsync();
    }
}
