using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Modulus.AuditLogging;
using Modulus.Core.Abstractions;
using Modulus.Core.Null;
using Modulus.MultiTenancy;
using Xunit;

namespace Modulus.Platform.Tests;

/// <summary>
/// Spec for the audit-log pipeline: ambient capture, filtering, paging.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AuditLoggingTests
{
    private static (IServiceProvider Services, CurrentTenant Tenant) BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ICurrentUser, NullCurrentUser>();
        services.AddModulusAuditLogging();
        var provider = services.BuildServiceProvider();
        return (provider, (CurrentTenant)provider.GetRequiredService<ICurrentTenant>());
    }

    [Fact]
    public async Task Log_CapturesAmbientTenant()
    {
        var (services, tenant) = BuildServices();
        var tenantId = Guid.NewGuid();
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();
        var store = services.GetRequiredService<IAuditLogStore>();

        using (tenant.Change(new TenantInfo(tenantId, "acme")))
            await logger.LogAsync("Grant", "Permission", "orders.refund", "Granted");

        var result = await store.QueryAsync(new AuditLogQuery { TenantId = tenantId });
        result.TotalCount.Should().Be(1);
        result.Items[0].Action.Should().Be("Grant");
        result.Items[0].TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task Query_FiltersAndPages()
    {
        var (services, _) = BuildServices();
        var store = services.GetRequiredService<IAuditLogStore>();
        var userId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
            await store.AppendAsync(new AuditLogEntry
            {
                OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-i),
                UserId = userId,
                Action = i % 2 == 0 ? "Login" : "Logout",
                Resource = "Session",
            });

        var logins = await store.QueryAsync(new AuditLogQuery { Action = "login", PageSize = 10 });
        logins.TotalCount.Should().Be(3, "action match is case-insensitive");

        var page = await store.QueryAsync(new AuditLogQuery { UserId = userId, Page = 2, PageSize = 2 });
        page.TotalCount.Should().Be(5);
        page.Items.Should().HaveCount(2);
        page.TotalPages.Should().Be(3);

        var missing = await store.QueryAsync(new AuditLogQuery { UserId = Guid.NewGuid() });
        missing.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Store_IsBounded_DropsOldest()
    {
        var (services, _) = BuildServices();
        var store = services.GetRequiredService<IAuditLogStore>();

        for (var i = 0; i < InMemoryAuditLogStore.MaxEntries + 10; i++)
            await store.AppendAsync(new AuditLogEntry
            {
                OccurredAt = DateTimeOffset.UtcNow,
                Action = "Tick",
            });

        var result = await store.QueryAsync(new AuditLogQuery { PageSize = 200 });
        result.TotalCount.Should().Be(InMemoryAuditLogStore.MaxEntries);
    }
}
