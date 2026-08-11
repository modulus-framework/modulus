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
            await context.SaveChangesAsync();

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
            await context.SaveChangesAsync();

            logger.LogInformation("Inventory module seeding completed: {WarehouseCount} warehouses and {StockCount} stock records added",
                warehouses.Length, stocks.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Inventory module");
            throw;
        }
    }
}
