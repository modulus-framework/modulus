using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.MultiTenancy.EntityFrameworkCore;
using Modulus.MultiTenancy.Extensions;
using Xunit;

namespace Modulus.EFCore.Integration.Tests;

/// <summary>
/// The seat limit under concurrent adds on a server database (PostgreSQL): two nodes add people to a company with one seat at once.
/// Exactly one add may win the seat; the check and the insert must not interleave between nodes.
/// </summary>
[Collection("PostgreSQL Integration Tests")]
public sealed class TenantSeatRacePostgresTests(PostgreSqlFixture fixture)
{
    private ServiceProvider Node()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMultiTenancy(t => t.UseHeaderResolver());
        services.AddEfCoreTenantStore(o => o.UseNpgsql(fixture.ConnectionString));
        return services.BuildServiceProvider();
    }

    private static async Task<bool> TryAddAsync(ServiceProvider node, Guid user, Guid tenantId)
    {
        try
        {
            using var scope = node.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<TenantManager>().AddMemberAsync(user, tenantId);
        }
        catch (ConflictException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Concurrent_adds_from_two_nodes_fill_a_one_seat_company_exactly_once()
    {
        var writer = Node();
        var reader = Node();
        await using (var scope = writer.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TenantStoreDbContext>().Database.EnsureCreatedAsync();

        var tenant = await writer.GetRequiredService<TenantManager>().CreateAsync($"seat-{Guid.NewGuid():N}", "Seat Co");
        await writer.GetRequiredService<TenantManager>().SetMaxUsersAsync(tenant.TenantId, 1);

        var attempts = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        var outcomes = await Task.WhenAll(attempts.Select((user, i) => TryAddAsync(i % 2 == 0 ? writer : reader, user, tenant.TenantId)));

        outcomes.Count(ok => ok).Should().Be(1, "the company has one seat; exactly one add wins it");
        await using var check = writer.CreateAsyncScope();
        var seated = await check.ServiceProvider.GetRequiredService<TenantStoreDbContext>().TenantMemberships
            .CountAsync(m => m.TenantId == tenant.TenantId && m.IsActive);
        seated.Should().Be(1);
    }
}
