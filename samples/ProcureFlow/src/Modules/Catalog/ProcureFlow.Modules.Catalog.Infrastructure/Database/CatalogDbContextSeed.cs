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
            await context.CommitAsync();

            logger.LogInformation("Catalog module seeding completed: {ProductCount} products added", products.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Catalog module");
            throw;
        }
    }

    public static async Task SeedEnhancedAsync(
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
                // Widget products with varying margins for field security demo
                Product.Create(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "Acme Widget",
                    100.00m,
                    150.00m,
                    tenantId,
                    "seed").Value,

                Product.Create(
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    "Premium Widget",
                    175.00m,
                    250.00m,
                    tenantId,
                    "seed").Value,

                // Gadget products
                Product.Create(
                    Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    "Universal Gadget",
                    120.00m,
                    180.00m,
                    tenantId,
                    "seed").Value,

                Product.Create(
                    Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                    "Smart Gadget",
                    240.00m,
                    320.00m,
                    tenantId,
                    "seed").Value,

                // Component products
                Product.Create(
                    Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                    "Standard Component",
                    30.00m,
                    45.00m,
                    tenantId,
                    "seed").Value,

                Product.Create(
                    Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                    "Advanced Component",
                    60.00m,
                    85.00m,
                    tenantId,
                    "seed").Value,

                // Kit products
                Product.Create(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "Starter Kit",
                    350.00m,
                    500.00m,
                    tenantId,
                    "seed").Value,

                Product.Create(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "Professional Kit",
                    800.00m,
                    1200.00m,
                    tenantId,
                    "seed").Value,

                // Accessory products
                Product.Create(
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    "Standard Accessory",
                    50.00m,
                    75.00m,
                    tenantId,
                    "seed").Value,

                Product.Create(
                    Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    "Premium Accessory",
                    100.00m,
                    150.00m,
                    tenantId,
                    "seed").Value,
            };

            context.Products.AddRange(products);
            await context.CommitAsync();

            logger.LogInformation("Enhanced Catalog module seeding completed: {ProductCount} products with field security attributes", products.Length);
            logger.LogInformation("  Products with [Classified] cost/margin: 10 items for Finance role testing");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding enhanced Catalog module");
            throw;
        }
    }
}
