using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModulusSample.Modules.Inventory.Domain.Entities;

namespace ModulusSample.Modules.Inventory.Infrastructure.Database;

public static class InventoryDbContextSeed
{
    public static async Task SeedAsync(
        InventoryDbContext context,
        ILogger logger,
        Guid tenantId,
        Guid orgUnitId)
    {
        try
        {
            if (await context.Warehouses.AnyAsync())
                return;

            var warehouses = new[]
            {
                Warehouse.Create(Guid.NewGuid(), "WH-EAST", "Eastern Warehouse", "100 East Blvd", "New York", "10001", "USA", orgUnitId, tenantId).Value,
                Warehouse.Create(Guid.NewGuid(), "WH-WEST", "Western Warehouse", "200 West Ave", "Los Angeles", "90001", "USA", orgUnitId, tenantId).Value,
                Warehouse.Create(Guid.NewGuid(), "WH-CENTRAL", "Central Hub", "300 Center Dr", "Chicago", "60601", "USA", orgUnitId, tenantId).Value,
            };

            context.Warehouses.AddRange(warehouses);
            await context.CommitAsync();

            // Add stock for each warehouse
            var stocks = new List<Stock>();
            foreach (var warehouse in warehouses)
            {
                for (int i = 1; i <= 5; i++)
                {
                    var stock = Stock.Create(
                        Guid.NewGuid(),
                        Guid.NewGuid(), // Random product ID (will be matched in real scenario)
                        warehouse.Id,
                        100 + (i * 50),
                        20,
                        50,
                        tenantId).Value;
                    stocks.Add(stock);
                }
            }

            context.Stocks.AddRange(stocks);
            await context.CommitAsync();

            logger.LogInformation("Inventory module seeding completed: {WarehouseCount} warehouses and {StockCount} stock records added",
                warehouses.Length, stocks.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Inventory module");
            throw;
        }
    }

    public static async Task SeedEnhancedAsync(
        InventoryDbContext context,
        ILogger logger,
        Guid tenantId,
        Guid nycWarehouseOrgId,
        Guid bostonWarehouseOrgId,
        Guid miamiWarehouseOrgId,
        Guid atlantaWarehouseOrgId)
    {
        try
        {
            if (await context.Warehouses.AnyAsync())
                return;

            // Warehouses with org unit assignments for scope filtering demo
            var warehouses = new[]
            {
                Warehouse.Create(
                    Guid.Parse("d0000000-0000-0000-0000-000000000001"),
                    "NYC-001",
                    "New York Distribution Center",
                    "350 Fifth Avenue, New York, NY 10118",
                    "New York",
                    "10118",
                    "USA",
                    nycWarehouseOrgId,
                    tenantId).Value,

                Warehouse.Create(
                    Guid.Parse("d0000000-0000-0000-0000-000000000002"),
                    "BOS-001",
                    "Boston Regional Hub",
                    "200 Clarendon Street, Boston, MA 02116",
                    "Boston",
                    "02116",
                    "USA",
                    bostonWarehouseOrgId,
                    tenantId).Value,

                Warehouse.Create(
                    Guid.Parse("d0000000-0000-0000-0000-000000000003"),
                    "MIA-001",
                    "Miami Distribution Center",
                    "100 Biscayne Boulevard, Miami, FL 33132",
                    "Miami",
                    "33132",
                    "USA",
                    miamiWarehouseOrgId,
                    tenantId).Value,

                Warehouse.Create(
                    Guid.Parse("d0000000-0000-0000-0000-000000000004"),
                    "ATL-001",
                    "Atlanta Regional Hub",
                    "3280 Peachtree Road, Atlanta, GA 30305",
                    "Atlanta",
                    "30305",
                    "USA",
                    atlantaWarehouseOrgId,
                    tenantId).Value,
            };

            context.Warehouses.AddRange(warehouses);
            await context.CommitAsync();

            // Stock levels set up for saga scenarios (some products with limited stock)
            var productIds = new[]
            {
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), // Widget - high stock
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), // Premium Widget - high stock
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), // Universal Gadget - medium stock
                Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), // Smart Gadget - low stock for scenario
                Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), // Standard Component - high stock
                Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), // Advanced Component - medium stock
                Guid.Parse("11111111-1111-1111-1111-111111111111"), // Starter Kit - limited stock for scenario
                Guid.Parse("22222222-2222-2222-2222-222222222222"), // Professional Kit - low stock for scenario
                Guid.Parse("33333333-3333-3333-3333-333333333333"), // Standard Accessory - high stock
                Guid.Parse("44444444-4444-4444-4444-444444444444"), // Premium Accessory - medium stock
            };

            var stocks = new List<Stock>();
            foreach (var warehouse in warehouses)
            {
                // Different stock levels per warehouse to demonstrate scope filtering
                var stockMultiplier = warehouse.Name.Contains("NYC") ? 1.0 :
                                     warehouse.Name.Contains("BOS") ? 0.8 :
                                     warehouse.Name.Contains("MIA") ? 0.6 : 0.4;

                for (int i = 0; i < productIds.Length; i++)
                {
                    // Set up some products with limited stock for saga failure scenario
                    int availableQuantity = i switch
                    {
                        3 => 2,  // Smart Gadget - very low stock for insufficient stock scenario
                        6 => 3,  // Professional Kit - low stock for scenario
                        7 => 5,  // Standard Accessory - medium low stock
                        _ => (int)(100 * stockMultiplier) // Normal stock levels
                    };

                    var stock = Stock.Create(
                        Guid.NewGuid(),
                        productIds[i],
                        warehouse.Id,
                        availableQuantity,
                        0,  // Reserved quantity starts at 0
                        20,  // Reorder point
                        tenantId).Value;
                    stocks.Add(stock);
                }
            }

            context.Stocks.AddRange(stocks);
            await context.CommitAsync();

            logger.LogInformation("Enhanced Inventory module seeding completed:");
            logger.LogInformation("  Warehouses: {WarehouseCount} with org unit assignments", warehouses.Length);
            logger.LogInformation("  Stock Records: {StockCount} across all warehouses", stocks.Count);
            logger.LogInformation("  Scenario Setup: Smart Gadget (2 units), Professional Kit (3 units) for insufficient stock saga demo");
            logger.LogInformation("  Org Scope: Warehouses assigned to NYC/Boston/Miami/Atlanta for filtering demo");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding enhanced Inventory module");
            throw;
        }
    }
}
