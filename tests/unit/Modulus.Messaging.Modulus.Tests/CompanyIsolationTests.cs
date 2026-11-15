using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Null;
using Modulus.EntityFrameworkCore;
using Modulus.Events;
using Modulus.Messaging.Testing;
using Modulus.Messaging.Transports.InMemory;
using Xunit;

namespace Modulus.Messaging.Modulus.Tests;

/// <summary>
/// The BRS acceptance "a consumer for tenant A cannot read tenant B rows", end to end: a message carries its company,
/// the adapter verifies and enters it, and the module's EF tenant filter does the rest (FR-21).
/// </summary>
[Trait("Category", "Unit")]
public sealed class CompanyIsolationTests
{
    private static readonly Guid CompanyA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid CompanyB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task FR_21_a_consumer_for_company_a_cannot_read_company_b_rows()
    {
        await using var database = await SqliteDatabase.OpenAsync();
        var tenant = new FakeCurrentTenant { FailClosed = true };
        var counts = new ConcurrentQueue<int>();
        await using var harness = await MessagingTestHarness.StartAsync(
            b => b.AddConsumer<CountProductsConsumer, Invoice>("billing.isolation").UseInMemory(),
            services => services
                .AddLogging()
                .AddSingleton(tenant)
                .AddSingleton<ICurrentTenant>(sp => sp.GetRequiredService<FakeCurrentTenant>())
                .AddSingleton<ICurrentUser, NullCurrentUser>()
                .AddScoped<DomainEventDispatcher>()
                .AddScoped(sp => new IsolationDbContext(
                    new DbContextOptionsBuilder<IsolationDbContext>().UseSqlite(database.Connection).Options,
                    sp.GetRequiredService<ICurrentTenant>(),
                    sp.GetRequiredService<ICurrentUser>(),
                    sp.GetRequiredService<DomainEventDispatcher>(),
                    sp))
                .AddSingleton(new CountLog(counts))
                .AddSingleton<ITenantContextRestorer>(new FakeRestorer(CompanyA, CompanyB))
                .AddSingleton<IConsumeScope, ModulusTenantConsumeScope>());

        using (var scope = harness.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IsolationDbContext>().Database.EnsureCreatedAsync();
        }

        await SeedAsync(harness, tenant, CompanyA, "product of A");
        await SeedAsync(harness, tenant, CompanyB, "product of B");

        using (MessageTenant.Enter(CompanyA.ToString("N")))
        {
            await harness.Bus.Send(new Uri("queue:billing.isolation"), new Invoice(Guid.NewGuid()));
        }

        using (MessageTenant.Enter(CompanyB.ToString("N")))
        {
            await harness.Bus.Send(new Uri("queue:billing.isolation"), new Invoice(Guid.NewGuid()));
        }

        (await MessagingTestHarness.WaitUntilAsync(() => counts.Count == 2)).Should().BeTrue();
        counts.ToArray().Should().BeEquivalentTo([1, 1], options => options.WithStrictOrdering(),
            "each company's consumer reads its own one row; a count of 2 would mean it read the other company's row too");
    }

    private static async Task SeedAsync(MessagingTestHarness harness, FakeCurrentTenant tenant, Guid company, string name)
    {
        using var scope = harness.Services.CreateScope();
        using var entered = tenant.Change(new TenantInfo(company, "seed"));
        var db = scope.ServiceProvider.GetRequiredService<IsolationDbContext>();
        db.Products.Add(new IsolatedProduct { Id = Guid.NewGuid(), Name = name, TenantId = company });
        await db.SaveChangesAsync();
    }

    /// <summary>A file-less in-memory SQLite database, kept open for the test so every scope sees the same tables.</summary>
    private sealed class SqliteDatabase : IAsyncDisposable
    {
        private SqliteDatabase(SqliteConnection connection) => Connection = connection;

        public SqliteConnection Connection { get; }

        public static async Task<SqliteDatabase> OpenAsync()
        {
            var connection = new SqliteConnection($"DataSource=isolation-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
            await connection.OpenAsync();
            return new SqliteDatabase(connection);
        }

        public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
    }
}

public sealed class IsolationDbContext(
    DbContextOptions<IsolationDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
{
    protected override string TablePrefix => "iso_";

    public DbSet<IsolatedProduct> Products => Set<IsolatedProduct>();
}

public sealed class IsolatedProduct : IHasTenantId
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid TenantId { get; set; }
}

public sealed class CountLog(ConcurrentQueue<int> counts)
{
    public ConcurrentQueue<int> Counts { get; } = counts;
}

/// <summary>Counts the products the consumer's company can read.</summary>
public sealed class CountProductsConsumer(IsolationDbContext db, CountLog log) : IConsumer<Invoice>
{
    public async Task Consume(ConsumeContext<Invoice> context)
    {
        log.Counts.Enqueue(await db.Products.CountAsync(context.CancellationToken));
    }
}
