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

            var orders = new List<SalesOrder>();

            for (int i = 1; i <= 5; i++)
            {
                var result = SalesOrder.Create(
                    Guid.NewGuid(),
                    $"SO-{DateTime.UtcNow.Year}-{i:D5}",
                    Guid.NewGuid(), // Random customer ID
                    orgUnitId,
                    tenantId);

                if (result.IsSuccess)
                {
                    var order = result.Value;

                    // Add some lines
                    for (int j = 1; j <= 3; j++)
                    {
                        order.AddLine(
                            Guid.NewGuid(), // Random product ID
                            10 + (j * 5),
                            100m + (j * 50m));
                    }

                    order.Confirm();
                    orders.Add(order);
                }
            }

            context.Orders.AddRange(orders);
            await context.CommitAsync();

            logger.LogInformation("Sales module seeding completed: {OrderCount} orders added", orders.Count);
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
        Guid salesRepUserId,
        Guid nycWarehouseOrgId)
    {
        try
        {
            if (await context.Orders.AnyAsync())
                return;

            // Customer IDs from Partners module
            var acmeCustomerId = Guid.Parse("cust0001-0000-0000-0000-000000000001");
            var globalDistCustomerId = Guid.Parse("cust0002-0000-0000-0000-000000000002");
            var southernWholesaleCustomerId = Guid.Parse("cust0003-0000-0000-0000-000000000003");
            var atlanticTradingCustomerId = Guid.Parse("cust0004-0000-0000-0000-000000000004");

            // Product IDs from Catalog module
            var widgetAProductId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var premiumWidgetProductId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var universalGadgetProductId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
            var smartGadgetProductId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
            var starterKitProductId = Guid.Parse("11111111-1111-1111-1111-111111111111");

            var orders = new List<SalesOrder>();

            // Create orders with proper customer and product relationships
            for (int i = 1; i <= 5; i++)
            {
                var customerId = i % 4 == 0 ? atlanticTradingCustomerId :
                                  i % 3 == 0 ? southernWholesaleCustomerId :
                                  i % 2 == 0 ? globalDistCustomerId : acmeCustomerId;

                var result = SalesOrder.Create(
                    Guid.NewGuid(),
                    $"SO-{DateTime.UtcNow.Year}-{i:D5}",
                    customerId,
                    nycWarehouseOrgId,
                    tenantId);

                if (result.IsSuccess)
                {
                    var order = result.Value;

                    // Add realistic order lines
                    order.AddLine(widgetAProductId, 10, 150.00m);
                    order.AddLine(premiumWidgetProductId, 5, 250.00m);

                    if (i <= 3) // Add third line for first 3 orders
                    {
                        order.AddLine(universalGadgetProductId, 3, 180.00m);
                    }

                    order.Confirm();
                    orders.Add(order);
                }
            }

            // Add one order with limited stock product for saga failure scenario
            var limitedStockResult = SalesOrder.Create(
                Guid.NewGuid(),
                $"SO-{DateTime.UtcNow.Year}-99999",
                globalDistCustomerId,
                nycWarehouseOrgId,
                tenantId);

            if (limitedStockResult.IsSuccess)
            {
                var limitedStockOrder = limitedStockResult.Value;
                // This will fail the saga due to insufficient stock
                limitedStockOrder.AddLine(smartGadgetProductId, 10, 320.00m); // Only 2 units available
                limitedStockOrder.AddLine(starterKitProductId, 5, 500.00m); // Only 3 units available
                limitedStockOrder.Confirm();
                orders.Add(limitedStockOrder);
            }

            context.Orders.AddRange(orders);
            await context.CommitAsync();

            logger.LogInformation("Enhanced Sales module seeding completed:");
            logger.LogInformation("  Orders: {OrderCount} orders with realistic customer/product relationships", orders.Count);
            logger.LogInformation("  Saga Setup: Order SO-99999 with limited stock items for failure scenario");
            logger.LogInformation("  Org Scope: All orders assigned to NYC warehouse for filtering demo");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding enhanced Sales module");
            throw;
        }
    }
}
