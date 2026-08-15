using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModulusSample.Modules.Sales.Domain.Entities;

namespace ModulusSample.Modules.Sales.Infrastructure.Database;

public static class SalesDbContextSeed
{
    public static async Task SeedAsync(
        SalesDbContext context,
        ILogger logger,
        Guid tenantId,
        Guid orgUnitId)
    {
        try
        {
            if (await context.Orders.AnyAsync())
                return;

            var orders = new[]
            {
                SalesOrder.Create(Guid.NewGuid(), "ORD-2024-001", Guid.NewGuid(), orgUnitId, tenantId).Value,
                SalesOrder.Create(Guid.NewGuid(), "ORD-2024-002", Guid.NewGuid(), orgUnitId, tenantId).Value,
                SalesOrder.Create(Guid.NewGuid(), "ORD-2024-003", Guid.NewGuid(), orgUnitId, tenantId).Value,
            };

            context.Orders.AddRange(orders);
            await context.CommitAsync();

            logger.LogInformation("Sales module seeding completed: {OrderCount} orders added", orders.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Sales module");
            throw;
        }
    }

    public static async Task SeedEnhancedAsync(
        SalesDbContext context,
        ILogger logger,
        Guid tenantId,
        Guid salesRepId,
        Guid orgUnitId)
    {
        try
        {
            if (await context.Orders.AnyAsync())
                return;

            var orders = new[]
            {
                // Completed orders
                SalesOrder.Create(
                    Guid.Parse("a0000000-0000-0000-0000-000000000001"),
                    "ORD-2024-001",
                    Guid.Parse("c0000000-0000-0000-0000-000000000001"),
                    orgUnitId,
                    tenantId).Value,

                SalesOrder.Create(
                    Guid.Parse("a0000000-0000-0000-0000-000000000002"),
                    "ORD-2024-002",
                    Guid.Parse("c0000000-0000-0000-0000-000000000002"),
                    orgUnitId,
                    tenantId).Value,

                // Confirmed orders
                SalesOrder.Create(
                    Guid.Parse("a0000000-0000-0000-0000-000000000003"),
                    "ORD-2024-003",
                    Guid.Parse("c0000000-0000-0000-0000-000000000003"),
                    orgUnitId,
                    tenantId).Value,

                SalesOrder.Create(
                    Guid.Parse("a0000000-0000-0000-0000-000000000004"),
                    "ORD-2024-004",
                    Guid.Parse("c0000000-0000-0000-0000-000000000004"),
                    orgUnitId,
                    tenantId).Value,

                // Draft order
                SalesOrder.Create(
                    Guid.Parse("a0000000-0000-0000-0000-000000000005"),
                    "ORD-2024-005",
                    Guid.Parse("c0000000-0000-0000-0000-000000000001"),
                    orgUnitId,
                    tenantId).Value,

                // Cancelled order
                SalesOrder.Create(
                    Guid.Parse("a0000000-0000-0000-0000-000000000006"),
                    "ORD-2024-006",
                    Guid.Parse("c0000000-0000-0000-0000-000000000002"),
                    orgUnitId,
                    tenantId).Value,
            };

            foreach (var order in orders)
            {
                order.AddLine(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 3, 150.00m);
                order.AddLine(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 2, 180.00m);
                order.AddLine(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 1, 120.00m);
            }

            orders[0].Confirm();
            orders[1].Confirm();
            orders[2].Confirm();
            orders[3].Confirm();

            orders[5].Cancel();

            context.Orders.AddRange(orders);
            await context.CommitAsync();

            logger.LogInformation("Enhanced Sales module seeding completed: {OrderCount} orders with various statuses", orders.Length);
            logger.LogInformation("  Sales rep: {SalesRepId} owns the customers used", salesRepId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding enhanced Sales module");
            throw;
        }
    }
}
