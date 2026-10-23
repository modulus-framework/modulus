namespace Modulus.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;
using Modulus.Core.Null;
using Modulus.Data.Abstractions;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events;
using FluentAssertions;
using Xunit;

/// <summary>
/// Keyset paging in a single shared database. Tenant isolation comes from the tenant query filter, not from the
/// paging code, so a seek position taken in one company must not reveal or pull in another company's rows.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SharedDatabaseKeysetTests : IAsyncDisposable
{
    private static readonly Guid CompanyA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _root;
    private readonly MutableTenant _tenant = new();

    public SharedDatabaseKeysetTests()
    {
        _connection = new SqliteConnection($"DataSource=shared-keyset-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(_tenant);
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddModuleDatabase<SharedTicketCtx>(o => o.UseSqlite(_connection));
        _root = services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_page_for_one_company_lists_only_its_rows_and_the_count_is_not_needed()
    {
        await SeedAsync();

        _tenant.Set(CompanyA);
        using var scope = _root.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IReadRepository<SharedTicket>>();

        var page = await repo.ListKeysetAsync(new AllTickets(), t => t.Id, t => t.Id, null, 10, default);

        page.Items.Should().Equal(1, 2, 3, 4, 5);
        page.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task A_position_taken_in_one_company_pulls_in_no_rows_of_another_company()
    {
        await SeedAsync();

        // Company A pages through its first two rows and keeps the position of the second.
        _tenant.Set(CompanyA);
        using (var a = _root.CreateScope())
        {
            var repoA = a.ServiceProvider.GetRequiredService<IReadRepository<SharedTicket>>();
            var firstA = await repoA.ListKeysetAsync(new AllTickets(), t => t.Id, t => t.Id, null, 2, default);
            firstA.Items.Should().Equal(1, 2);
            firstA.NextCursor.Should().NotBeNull();
        }

        // Company B takes the same position. Its own rows after that key are 6..8. A's rows 3..5 sit in the same
        // table but must not appear for B, and the position must not make B see A's data.
        _tenant.Set(CompanyB);
        using var b = _root.CreateScope();
        var repoB = b.ServiceProvider.GetRequiredService<IReadRepository<SharedTicket>>();
        var seekB = await repoB.ListKeysetAsync(new AllTickets(), t => t.Id, t => t.Id, 2, 10, default);

        seekB.Items.Should().Equal(6, 7, 8);
        seekB.Items.Should().OnlyContain(id => id >= 6);
    }

    private async Task SeedAsync()
    {
        using var scope = _root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SharedTicketCtx>();
        await db.Database.EnsureCreatedAsync();

        _tenant.Set(CompanyA);
        for (var id = 1; id <= 5; id++)
            db.Tickets.Add(new SharedTicket { Id = id, TenantId = CompanyA });
        await db.SaveChangesAsync();

        _tenant.Set(CompanyB);
        for (var id = 6; id <= 8; id++)
            db.Tickets.Add(new SharedTicket { Id = id, TenantId = CompanyB });
        await db.SaveChangesAsync();
    }

    private sealed class MutableTenant : ICurrentTenant
    {
        public Guid? TenantId { get; private set; }
        public string? TenantSlug => TenantId?.ToString();
        public bool IsAvailable => TenantId is not null;
        public bool IsHost { get; private set; } = true;

        public void Set(Guid id) { TenantId = id; IsHost = false; }

        public IDisposable Change(TenantInfo? tenant)
        {
            var (id, host) = (TenantId, IsHost);
            if (tenant is null) { TenantId = null; IsHost = true; } else Set(tenant.TenantId);
            return new Restore(() => { TenantId = id; IsHost = host; });
        }

        private sealed class Restore(Action undo) : IDisposable
        {
            public void Dispose() => undo();
        }
    }

    private sealed class SharedTicketCtx(
        DbContextOptions<SharedTicketCtx> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "shk_";
        public DbSet<SharedTicket> Tickets => Set<SharedTicket>();
    }

    private sealed class SharedTicket : IHasTenantId
    {
        public int Id { get; set; }
        public Guid TenantId { get; set; }
    }

    private sealed class AllTickets : Specification<SharedTicket>
    {
        public AllTickets() => Filter = _ => true;
    }

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
