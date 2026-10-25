namespace Modulus.EntityFrameworkCore.Tests;

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Null;
using Modulus.Events;
using Xunit;

/// <summary>
/// A soft-deletable entity with a derived type in the same table (TPH) must build: EF Core refuses query filters on derived types, and the
/// base type's filter already applies to them.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DerivedEntityFilterTests : IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly ServiceProvider _root;

    public DerivedEntityFilterTests()
    {
        _conn = new SqliteConnection($"DataSource=derived-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _conn.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant>(_ => new NullCurrentTenant());
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped(sp => new DocsDbContext(
            new DbContextOptionsBuilder<DocsDbContext>().UseSqlite(_conn).Options,
            sp.GetRequiredService<ICurrentTenant>(),
            sp.GetRequiredService<ICurrentUser>(),
            sp.GetRequiredService<DomainEventDispatcher>(),
            sp));
        _root = services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_derived_type_in_the_same_table_builds_and_the_base_filter_applies()
    {
        using var scope = _root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocsDbContext>();

        await db.Database.EnsureCreatedAsync();
        db.Docs.Add(new Invoice { Id = Guid.NewGuid(), Total = 10 });
        db.Docs.Add(new Invoice { Id = Guid.NewGuid(), Total = 20, IsDeleted = true });
        await db.SaveChangesAsync();

        (await db.Invoices.CountAsync()).Should().Be(1, "the soft-deleted invoice is filtered out through the base type");
    }

    [Fact]
    public void A_derived_marker_the_root_lacks_is_refused_when_the_model_is_built()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<LeakyDbContext>().UseSqlite(conn).Options;
        var sp = new ServiceCollection().AddLogging().BuildServiceProvider();

        var act = () =>
        {
            using var db = new LeakyDbContext(options, new NullCurrentTenant(), new NullCurrentUser(), new DomainEventDispatcher(sp), sp);
            _ = db.Model;
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*Credit*Charge*root*");
    }

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private sealed class DocsDbContext(
        DbContextOptions<DocsDbContext> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "derived_";
        public DbSet<Doc> Docs => Set<Doc>();
        public DbSet<Invoice> Invoices => Set<Invoice>();
    }

    private abstract class Doc : ISoftDelete
    {
        public Guid Id { get; set; }
        public bool IsDeleted { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }

    private sealed class Invoice : Doc
    {
        public decimal Total { get; set; }
    }
}

internal sealed class LeakyDbContext(
    DbContextOptions<LeakyDbContext> options,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    DomainEventDispatcher dispatcher,
    IServiceProvider sp)
    : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
{
    protected override string TablePrefix => "leaky_";
    public DbSet<Charge> Charges => Set<Charge>();
    public DbSet<Credit> Credits => Set<Credit>();
}

internal class Charge
{
    public Guid Id { get; set; }
}

internal sealed class Credit : Charge, ISoftDelete
{
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
