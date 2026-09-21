using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.MultiTenancy;
using Modulus.Notifications;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Spec for the notification center: publishing, unread filtering,
/// read claims, and recipient isolation.
/// </summary>
[Trait("Category", "Unit")]
public sealed class NotificationsTests
{
    private static (IServiceProvider Services, CurrentTenant Tenant) BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddModulusNotifications();
        var provider = services.BuildServiceProvider();
        return (provider, (CurrentTenant)provider.GetRequiredService<ICurrentTenant>());
    }

    [Fact]
    public async Task Publish_StampsAmbientTenant()
    {
        var (services, tenant) = BuildServices();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        using var scope = services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
        var store = services.GetRequiredService<INotificationStore>();

        using (tenant.Change(new TenantInfo(tenantId, "acme")))
            await publisher.PublishAsync(userId, "Welcome", severity: NotificationSeverity.Success);

        var list = await store.ListAsync(userId, tenantId);
        list.TotalCount.Should().Be(1);
        list.Items[0].TenantId.Should().Be(tenantId);
        list.Items[0].IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task List_UnreadOnly_AndMarkAllAsRead()
    {
        var (services, _) = BuildServices();
        var userId = Guid.NewGuid();
        using var scope = services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
        var store = services.GetRequiredService<INotificationStore>();

        var first = await publisher.PublishAsync(userId, "One");
        await publisher.PublishAsync(userId, "Two");
        (await store.MarkAsReadAsync(first.Id, userId)).Should().BeTrue();

        var unread = await store.ListAsync(userId, unreadOnly: true);
        unread.TotalCount.Should().Be(1);
        unread.Items[0].Title.Should().Be("Two");

        (await store.MarkAllAsReadAsync(userId)).Should().Be(1);
        (await store.ListAsync(userId, unreadOnly: true)).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task MarkAsRead_WrongUser_FailsClosed()
    {
        var (services, _) = BuildServices();
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        using var scope = services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
        var store = services.GetRequiredService<INotificationStore>();

        var notification = await publisher.PublishAsync(owner, "Secret");

        (await store.MarkAsReadAsync(notification.Id, stranger)).Should().BeFalse();
        (await store.DeleteAsync(notification.Id, stranger)).Should().BeFalse();
        (await store.GetOrNullAsync(notification.Id))!.IsRead.Should().BeFalse();
    }
}
