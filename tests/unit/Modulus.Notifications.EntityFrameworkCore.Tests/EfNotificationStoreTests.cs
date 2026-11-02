using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Notifications;
using Modulus.Notifications.EntityFrameworkCore;
using Xunit;

namespace Modulus.Notifications.EntityFrameworkCore.Tests;

/// <summary>The durable inbox and preferences: a person's inbox is theirs in their company, read claims are atomic, and registration replaces the in-memory stores.</summary>
[Trait("Category", "Unit")]
public sealed class EfNotificationStoreTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private DbContextOptions<TestNotificationsContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<TestNotificationsContext>().UseSqlite(_connection).Options;
        await using var db = new TestNotificationsContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task A_notification_round_trips_and_is_listed_for_its_person_and_company_only()
    {
        var store = new EfNotificationStore(Db());
        var alice = Guid.NewGuid();
        var company = Guid.NewGuid();
        var n = Note(company, alice, "Shipped", "orders.shipped");
        await store.InsertAsync(n);

        var stored = await store.GetOrNullAsync(n.Id);
        stored!.Title.Should().Be("Shipped");
        stored.Category.Should().Be("orders.shipped");
        stored.CreatedAt.Should().Be(n.CreatedAt);
        stored.ReadAt.Should().BeNull();

        (await store.ListAsync(alice, company)).TotalCount.Should().Be(1);
        (await store.ListAsync(Guid.NewGuid(), company)).TotalCount.Should().Be(0);
        (await store.ListAsync(alice, Guid.NewGuid())).TotalCount.Should().Be(0);
        (await store.ListAsync(alice, null)).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Marking_read_is_an_atomic_claim_and_only_the_recipient_can_claim_it()
    {
        var store = new EfNotificationStore(Db());
        var alice = Guid.NewGuid();
        var n = Note(null, alice, "One", null);
        await store.InsertAsync(n);

        (await store.MarkAsReadAsync(n.Id, Guid.NewGuid())).Should().BeFalse();
        (await store.MarkAsReadAsync(n.Id, alice)).Should().BeTrue();
        (await store.MarkAsReadAsync(n.Id, alice)).Should().BeFalse("a notification is claimed once");

        (await store.GetOrNullAsync(n.Id))!.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Mark_all_read_changes_only_the_unread_ones_of_that_company()
    {
        var store = new EfNotificationStore(Db());
        var alice = Guid.NewGuid();
        var company = Guid.NewGuid();
        await store.InsertAsync(Note(company, alice, "1", null));
        await store.InsertAsync(Note(company, alice, "2", null));
        await store.InsertAsync(Note(Guid.NewGuid(), alice, "other company", null));

        (await store.MarkAllAsReadAsync(alice, company)).Should().Be(2);
        (await store.ListAsync(alice, company, unreadOnly: true)).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Delete_removes_only_the_recipients_notification()
    {
        var store = new EfNotificationStore(Db());
        var alice = Guid.NewGuid();
        var n = Note(null, alice, "Mine", null);
        await store.InsertAsync(n);

        (await store.DeleteAsync(n.Id, Guid.NewGuid())).Should().BeFalse();
        (await store.DeleteAsync(n.Id, alice)).Should().BeTrue();
        (await store.GetOrNullAsync(n.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Preferences_are_stored_per_person_company_category_and_channel_and_update_in_place()
    {
        var store = new EfNotificationPreferenceStore(Db());
        var alice = Guid.NewGuid();
        var company = Guid.NewGuid();

        (await store.GetAsync(company, alice, "orders.shipped", NotificationChannel.InApp)).Should().BeNull();

        await store.SetAsync(company, alice, "orders.shipped", NotificationChannel.InApp, false);
        await store.SetAsync(company, alice, "orders.shipped", NotificationChannel.InApp, true);

        (await store.GetAsync(company, alice, "orders.shipped", NotificationChannel.InApp)).Should().BeTrue();
        (await store.GetAsync(Guid.NewGuid(), alice, "orders.shipped", NotificationChannel.InApp)).Should().BeNull();
        await using var db = Db();
        db.Preferences.Count().Should().Be(1, "the second write updates the row");
    }

    [Fact]
    public void Registration_replaces_the_in_memory_stores_whichever_order_it_is_added_in()
    {
        var services = new ServiceCollection();
        services.AddModulusNotifications();
        services.AddDbContext<TestNotificationsContext>(o => o.UseSqlite(_connection));
        services.AddModulusNotificationsStore<TestNotificationsContext>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<INotificationStore>().Should().BeOfType<EfNotificationStore>();
        scope.ServiceProvider.GetRequiredService<INotificationPreferenceStore>().Should().BeOfType<EfNotificationPreferenceStore>();
    }

    private TestNotificationsContext Db() => new(_options);

    private static UserNotification Note(Guid? company, Guid user, string title, string? category) => new()
    {
        TenantId = company,
        UserId = user,
        Title = title,
        Category = category,
        CreatedAt = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero).AddSeconds(Random.Shared.Next(0, 100000)),
    };

    private sealed class TestNotificationsContext(DbContextOptions<TestNotificationsContext> options) : ModulusNotificationsDbContext(options);
}
