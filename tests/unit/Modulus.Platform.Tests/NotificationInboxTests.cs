using FluentAssertions;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.MultiTenancy;
using Modulus.Notifications;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>Categorised sending honours channel choices; the inbox serves only the signed-in person, in their own company.</summary>
[Trait("Category", "Unit")]
public sealed class NotificationInboxTests
{
    private sealed class Harness
    {
        public InMemoryNotificationStore Store { get; } = new();
        public InMemoryNotificationPreferenceStore Preferences { get; } = new();
        public CurrentTenant Tenant { get; } = new();
        public FakeUser User { get; } = new();
        public DateTimeOffset Now { get; set; } = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

        public NotificationSender Sender() => new(Store, Preferences, Tenant, new FixedClock(this));
        public NotificationInbox Inbox() => new(Store, Preferences, Tenant, User);
    }

    private static NotificationRequest Shipped(Guid recipient) => new(recipient, "orders.shipped", "Order shipped", "On its way.");

    [Fact]
    public async Task A_categorised_send_is_stored_with_its_category_and_company()
    {
        var h = new Harness();
        var company = Guid.NewGuid();
        var alice = Guid.NewGuid();
        using var scope = h.Tenant.Change(new TenantInfo(company, "acme"));

        var delivery = await h.Sender().SendAsync(Shipped(alice));

        delivery.Delivered.Should().Equal(NotificationChannel.InApp);
        var stored = await h.Store.GetOrNullAsync(delivery.NotificationId!.Value);
        stored!.Category.Should().Be("orders.shipped");
        stored.TenantId.Should().Be(company);
        stored.UserId.Should().Be(alice);
    }

    [Fact]
    public async Task A_muted_category_stores_nothing_for_that_person_and_still_succeeds()
    {
        var h = new Harness();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        h.User.UserId = alice;
        await h.Inbox().SetPreferenceAsync("orders.shipped", NotificationChannel.InApp, false);

        var muted = await h.Sender().SendAsync(Shipped(alice));
        var open = await h.Sender().SendAsync(Shipped(bob));

        muted.Delivered.Should().BeEmpty();
        muted.NotificationId.Should().BeNull();
        open.Delivered.Should().Equal(NotificationChannel.InApp);
        (await h.Inbox().UnreadCountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("Orders.Shipped")]
    [InlineData("orders shipped")]
    [InlineData("orders..shipped")]
    [InlineData("../admin")]
    [InlineData("")]
    public async Task Malformed_categories_are_refused(string category)
    {
        var h = new Harness();

        var act = () => h.Sender().SendAsync(new NotificationRequest(Guid.NewGuid(), category, "x"));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task The_inbox_shows_only_the_signed_in_persons_notifications_in_their_company()
    {
        var h = new Harness();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        using (h.Tenant.Change(new TenantInfo(companyA, "a")))
        {
            await h.Sender().SendAsync(Shipped(alice));
            await h.Sender().SendAsync(Shipped(bob));
        }

        h.User.UserId = alice;
        using (h.Tenant.Change(new TenantInfo(companyA, "a")))
            (await h.Inbox().ListAsync()).TotalCount.Should().Be(1);

        using (h.Tenant.Change(new TenantInfo(companyB, "b")))
            (await h.Inbox().ListAsync()).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task A_notification_from_another_company_cannot_be_marked_read_even_by_its_recipient()
    {
        var h = new Harness();
        var alice = Guid.NewGuid();
        var companyA = Guid.NewGuid();
        Guid id;
        using (h.Tenant.Change(new TenantInfo(companyA, "a")))
            id = (await h.Sender().SendAsync(Shipped(alice))).NotificationId!.Value;

        h.User.UserId = alice;
        using (h.Tenant.Change(new TenantInfo(Guid.NewGuid(), "b")))
            await h.Inbox().Invoking(i => i.MarkReadAsync(id)).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Another_persons_notification_cannot_be_marked_read()
    {
        var h = new Harness();
        var alice = Guid.NewGuid();
        var id = (await h.Sender().SendAsync(Shipped(alice))).NotificationId!.Value;

        h.User.UserId = Guid.NewGuid();
        await h.Inbox().Invoking(i => i.MarkReadAsync(id)).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Marking_read_and_mark_all_update_the_unread_count()
    {
        var h = new Harness();
        var alice = Guid.NewGuid();
        var sender = h.Sender();
        var first = (await sender.SendAsync(Shipped(alice))).NotificationId!.Value;
        await sender.SendAsync(Shipped(alice));
        await sender.SendAsync(Shipped(alice));
        h.User.UserId = alice;
        var inbox = h.Inbox();

        await inbox.MarkReadAsync(first);
        (await inbox.UnreadCountAsync()).Should().Be(2);

        (await inbox.MarkAllReadAsync()).Should().Be(2);
        (await inbox.UnreadCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Channel_preferences_default_to_on_and_are_per_company()
    {
        var h = new Harness();
        var alice = Guid.NewGuid();
        h.User.UserId = alice;
        var inbox = h.Inbox();

        (await inbox.GetPreferenceAsync("orders.shipped", NotificationChannel.InApp)).Should().BeTrue();

        using (h.Tenant.Change(new TenantInfo(Guid.NewGuid(), "a")))
        {
            await inbox.SetPreferenceAsync("orders.shipped", NotificationChannel.InApp, false);
            (await inbox.GetPreferenceAsync("orders.shipped", NotificationChannel.InApp)).Should().BeFalse();
        }

        (await inbox.GetPreferenceAsync("orders.shipped", NotificationChannel.InApp)).Should().BeTrue();
    }

    [Fact]
    public async Task The_inbox_needs_a_signed_in_person()
    {
        var h = new Harness();

        await h.Inbox().Invoking(i => i.ListAsync()).Should().ThrowAsync<UnauthorizedException>();
        await h.Inbox().Invoking(i => i.UnreadCountAsync()).Should().ThrowAsync<UnauthorizedException>();
    }

    private sealed class FixedClock(Harness harness) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => harness.Now;
    }

    private sealed class FakeUser : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public string? UserName => null;
        public string? Email => null;
        public bool IsAuthenticated => UserId.HasValue;
        public bool IsInRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyList<string> Permissions => [];
    }
}
