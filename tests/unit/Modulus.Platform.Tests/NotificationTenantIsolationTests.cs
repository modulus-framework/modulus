using FluentAssertions;
using Modulus.Notifications;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// A notification inbox is per company: a request with no tenant resolved used to pass <c>tenantId: null</c> and
/// receive every company's notifications of the user.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NotificationTenantIsolationTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();

    private static async Task<InMemoryNotificationStore> SeedAsync()
    {
        var store = new InMemoryNotificationStore();
        foreach (var (tenant, title) in new[] { ((Guid?)CompanyA, "a"), (CompanyB, "b"), (null, "host") })
            await store.InsertAsync(new UserNotification { Id = Guid.NewGuid(), UserId = User, TenantId = tenant, Title = title });
        return store;
    }

    [Fact]
    public async Task Each_company_lists_only_its_own_notifications()
    {
        var store = await SeedAsync();

        (await store.ListAsync(User, CompanyA)).Items.Select(n => n.Title).Should().Equal("a");
        (await store.ListAsync(User, null)).Items.Select(n => n.Title).Should().Equal("host");
    }

    [Fact]
    public async Task Mark_all_read_stays_in_the_company()
    {
        var store = await SeedAsync();

        (await store.MarkAllAsReadAsync(User, null)).Should().Be(1);
        (await store.ListAsync(User, CompanyB, unreadOnly: true)).Items.Should().ContainSingle();
    }
}
