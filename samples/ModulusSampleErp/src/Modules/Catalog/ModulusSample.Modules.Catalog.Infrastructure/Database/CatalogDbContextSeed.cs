using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModulusSample.Modules.Catalog.Domain.Entities;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Catalog.Infrastructure.Database;

public static class CatalogDbContextSeed
{
    public static async Task SeedAsync(
        CatalogDbContext context,
        ILogger logger,
        Guid tenantId)
    {
        try
        {
            if (await context.Products.AnyAsync())
                return;

            var products = new[]
            {
                Product.Create(Guid.NewGuid(), "Widget A", 10m, 25m, tenantId, "seed").Value,
                Product.Create(Guid.NewGuid(), "Widget B", 15m, 35m, tenantId, "seed").Value,
                Product.Create(Guid.NewGuid(), "Gadget X", 50m, 120m, tenantId, "seed").Value,
                Product.Create(Guid.NewGuid(), "Gadget Y", 75m, 180m, tenantId, "seed").Value,
                Product.Create(Guid.NewGuid(), "Premium Kit", 200m, 500m, tenantId, "seed").Value,
            };

            context.Products.AddRange(products);
            await context.SaveChangesAsync();

            logger.LogInformation("Catalog module seeding completed: {ProductCount} products added", products.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Catalog module");
            throw;
        }
    }
}
