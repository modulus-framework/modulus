using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions;
using Modulus.Events.Abstractions;
using Modulus.Identity.IntegrationEvents;
using Modulus.MultiTenancy;
using Modulus.Notifications;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>The notification module's consumer of the identity module's password-changed event: the person is told in their inbox, and a muted category gets nothing.</summary>
[Trait("Category", "Unit")]
public sealed class PasswordChangedNotificationTests
{
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

    private static (IServiceProvider Services, CurrentTenant Tenant, FakeUser User) Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        var user = new FakeUser();
        services.AddSingleton<ICurrentUser>(user);
        services.AddModulusNotifications();
        var provider = services.BuildServiceProvider();
        return (provider, (CurrentTenant)provider.GetRequiredService<ICurrentTenant>(), user);
    }

    [Fact]
    public void The_notification_module_registers_a_handler_for_the_identity_event()
    {
        var (services, _, _) = Build();
        using var scope = services.CreateScope();

        var handlers = scope.ServiceProvider.GetServices<IIntegrationEventHandler<PasswordChangedIntegrationEvent>>().ToList();

        handlers.Should().ContainSingle().Which.Should().BeOfType<PasswordChangedNotificationHandler>();
    }

    [Fact]
    public async Task The_affected_person_is_told_in_their_inbox_with_a_warning()
    {
        var (services, tenant, user) = Build();
        var company = Guid.NewGuid();
        var alice = Guid.NewGuid();
        using var scope = services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<PasswordChangedIntegrationEvent>>();

        using (tenant.Change(new TenantInfo(company, "acme")))
            await handler.HandleAsync(new PasswordChangedIntegrationEvent(alice, DateTimeOffset.UtcNow), CancellationToken.None);

        user.UserId = alice;
        using (tenant.Change(new TenantInfo(company, "acme")))
        {
            var inbox = await scope.ServiceProvider.GetRequiredService<INotificationInbox>().ListAsync();
            inbox.TotalCount.Should().Be(1);
            inbox.Items[0].Category.Should().Be(PasswordChangedNotificationHandler.Category);
            inbox.Items[0].Severity.Should().Be(NotificationSeverity.Warning);
        }
    }

    [Fact]
    public async Task A_person_who_muted_the_category_is_not_told()
    {
        var (services, tenant, user) = Build();
        var alice = Guid.NewGuid();
        using var scope = services.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<INotificationInbox>();
        user.UserId = alice;
        await inbox.SetPreferenceAsync(PasswordChangedNotificationHandler.Category, NotificationChannel.InApp, false);
        var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<PasswordChangedIntegrationEvent>>();

        await handler.HandleAsync(new PasswordChangedIntegrationEvent(alice, DateTimeOffset.UtcNow), CancellationToken.None);

        (await inbox.ListAsync()).TotalCount.Should().Be(0);
    }
}
