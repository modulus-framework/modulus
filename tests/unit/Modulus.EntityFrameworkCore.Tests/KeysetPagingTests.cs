namespace Modulus.EntityFrameworkCore.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.Data.Abstractions;
using Modulus.EntityFrameworkCore.Extensions;
using Modulus.Events;
using FluentAssertions;
using Xunit;

/// <summary>Keyset (seek) paging on the EF Core repository: order by key, seek past the last key, no total count.</summary>
[Trait("Category", "Unit")]
public sealed class KeysetPagingTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _root;

    public KeysetPagingTests()
    {
        // A database of its own per test: a shared in-memory name would keep the rows of the tests that ran before.
        _connection = new SqliteConnection($"DataSource=keyset-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentTenant, HostTenant>();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddModuleDatabase<TicketCtx>(o => o.UseSqlite(_connection));
        _root = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Pages_walk_the_whole_set_in_key_order_and_the_last_page_has_no_next()
    {
        await SeedAsync(25);
        using var scope = _root.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IReadRepository<Ticket>>();

        var first = await repo.ListKeysetAsync(new AllTickets(), t => t.Id, t => t.Id, null, 10, default);
        first.Items.Should().Equal(Enumerable.Range(1, 10));
        first.HasNextPage.Should().BeTrue();

        var second = await repo.ListKeysetAsync(new AllTickets(), t => t.Id, t => t.Id, first.Items[^1], 10, default);
        second.Items.Should().Equal(Enumerable.Range(11, 10));
        second.HasNextPage.Should().BeTrue();

        var last = await repo.ListKeysetAsync(new AllTickets(), t => t.Id, t => t.Id, second.Items[^1], 10, default);
        last.Items.Should().Equal(Enumerable.Range(21, 5));
        last.HasNextPage.Should().BeFalse();
        last.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task A_cursor_decodes_to_the_position_of_the_last_item_on_the_page()
    {
        await SeedAsync(12);
        using var scope = _root.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IReadRepository<Ticket>>();

        var page = await repo.ListKeysetAsync(new AllTickets(), t => t.Id, t => t.Id, null, 5, default);

        page.NextCursor.Should().NotBeNull();
        var position = CursorCodec.Decode<int>(page.NextCursor!);
        position.Should().Be(5);

        var next = await repo.ListKeysetAsync(new AllTickets(), t => t.Id, t => t.Id, position, 5, default);
        next.Items.Should().Equal(Enumerable.Range(6, 5));
    }

    [Fact]
    public async Task The_filter_applies_before_the_window_so_a_page_is_never_short_because_of_it()
    {
        await SeedAsync(10);
        using var scope = _root.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IReadRepository<Ticket>>();

        var first = await repo.ListKeysetAsync(new EvenTickets(), t => t.Id, t => t.Id, null, 3, default);
        first.Items.Should().Equal(2, 4, 6);
        first.HasNextPage.Should().BeTrue();

        var last = await repo.ListKeysetAsync(new EvenTickets(), t => t.Id, t => t.Id, first.Items[^1], 3, default);
        last.Items.Should().Equal(8, 10);
        last.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task A_specification_with_skip_or_take_is_refused_because_the_key_is_the_position()
    {
        await SeedAsync(3);
        using var scope = _root.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IReadRepository<Ticket>>();

        var act = () => repo.ListKeysetAsync(new WindowedTickets(), t => t.Id, t => t.Id, null, 2, default);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Skip or Take*");
    }

    [Fact]
    public async Task A_guid_key_is_refused_because_it_cannot_be_ordered_by_position()
    {
        await SeedAsync(3);
        using var scope = _root.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IReadRepository<Ticket>>();

        var act = () => repo.ListKeysetAsync(new AllTickets(), t => t.Reference, t => t.Id, null, 2, default);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void A_malformed_cursor_is_an_argument_error_not_a_crash()
    {
        var act = () => CursorCodec.Decode<int>("not-a-cursor!!");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_cursor_for_another_type_is_an_argument_error()
    {
        var text = CursorCodec.Encode("a text key");

        var act = () => CursorCodec.Decode<int>(text);

        act.Should().Throw<ArgumentException>();
    }

    private async Task SeedAsync(int count)
    {
        using var scope = _root.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketCtx>();
        await db.Database.EnsureCreatedAsync();
        for (var id = 1; id <= count; id++)
            db.Tickets.Add(new Ticket { Id = id, Title = $"Ticket {id}" });
        await db.SaveChangesAsync();
    }

    private sealed class HostTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public string? TenantSlug => null;
        public bool IsAvailable => false;
        public bool IsHost => true;
        public IDisposable Change(TenantInfo? tenant) => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class TicketCtx(
        DbContextOptions<TicketCtx> options,
        ICurrentTenant currentTenant,
        ICurrentUser currentUser,
        DomainEventDispatcher dispatcher,
        IServiceProvider sp)
        : ModuleDbContext(options, currentTenant, currentUser, dispatcher, sp)
    {
        protected override string TablePrefix => "tkt_";
        public DbSet<Ticket> Tickets => Set<Ticket>();
    }

    private sealed class Ticket
    {
        public int Id { get; set; }
        public Guid Reference { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
    }

    private sealed class AllTickets : Specification<Ticket>
    {
        public AllTickets() => Filter = _ => true;
    }

    private sealed class EvenTickets : Specification<Ticket>
    {
        public EvenTickets() => Filter = t => t.Id % 2 == 0;
    }

    private sealed class WindowedTickets : Specification<Ticket>
    {
        public WindowedTickets()
        {
            Filter = _ => true;
            Skip = 1;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
