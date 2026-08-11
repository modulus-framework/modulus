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
            await context.SaveChangesAsync();

            logger.LogInformation("Sales module seeding completed: {OrderCount} orders added", orders.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Sales module");
            throw;
        }
    }
}
