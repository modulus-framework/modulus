using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModulusSample.Modules.Partners.Domain.Entities;

namespace ModulusSample.Modules.Partners.Infrastructure.Database;

public static class PartnersDbContextSeed
{
    public static async Task SeedAsync(
        PartnersDbContext context,
        ILogger logger,
        Guid tenantId,
        Guid ownerId)
    {
        try
        {
            if (await context.Partners.AnyAsync())
                return;

            var partners = new[]
            {
                Partner.Create(Guid.NewGuid(), "Acme Corp", "Customer", "contact@acme.com", "+1-555-0001", "123 Main St", ownerId, tenantId).Value,
                Partner.Create(Guid.NewGuid(), "Tech Supplies Ltd", "Supplier", "sales@techsupplies.com", "+1-555-0002", "456 Oak Ave", ownerId, tenantId).Value,
                Partner.Create(Guid.NewGuid(), "Global Distributors", "Customer", "orders@globaldist.com", "+1-555-0003", "789 Pine Rd", ownerId, tenantId).Value,
                Partner.Create(Guid.NewGuid(), "Premium Materials Inc", "Supplier", "procurement@premmat.com", "+1-555-0004", "321 Elm St", ownerId, tenantId).Value,
            };

            context.Partners.AddRange(partners);
            await context.CommitAsync();

            logger.LogInformation("Partners module seeding completed: {PartnerCount} partners added", partners.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Partners module");
            throw;
        }
    }

    public static async Task SeedEnhancedAsync(
        PartnersDbContext context,
        ILogger logger,
        Guid tenantId,
        Guid salesRepOwnerId)
    {
        try
        {
            if (await context.Partners.AnyAsync())
                return;

            // Customers with credit limits and realistic details for demo scenarios
            var customers = new[]
            {
                Partner.Create(
                    Guid.Parse("c0000000-0000-0000-0000-000000000001"),
                    "Acme Corporation",
                    "Customer",
                    "orders@acmecorp.com",
                    "+1-212-555-0101",
                    "350 Fifth Avenue, New York, NY 10118",
                    salesRepOwnerId,
                    tenantId).Value,

                Partner.Create(
                    Guid.Parse("c0000000-0000-0000-0000-000000000002"),
                    "Global Distributors LLC",
                    "Customer",
                    "sales@globaldist.com",
                    "+1-617-555-0202",
                    "200 Clarendon Street, Boston, MA 02116",
                    salesRepOwnerId,
                    tenantId).Value,

                Partner.Create(
                    Guid.Parse("c0000000-0000-0000-0000-000000000003"),
                    "Southern Wholesale Inc",
                    "Customer",
                    "orders@southernwholesale.com",
                    "+1-305-555-0303",
                    "100 Biscayne Boulevard, Miami, FL 33132",
                    salesRepOwnerId,
                    tenantId).Value,

                Partner.Create(
                    Guid.Parse("c0000000-0000-0000-0000-000000000004"),
                    "Atlantic Trading Co",
                    "Customer",
                    "purchasing@atlantictrading.com",
                    "+1-404-555-0404",
                    "3280 Peachtree Road, Atlanta, GA 30305",
                    salesRepOwnerId,
                    tenantId).Value,
            };

            // Suppliers for purchasing module scenarios
            var suppliers = new[]
            {
                Partner.Create(
                    Guid.Parse("b0000000-0000-0000-0000-000000000001"),
                    "Tech Supplies Ltd",
                    "Supplier",
                    "sales@techsupplies.com",
                    "+1-847-555-0101",
                    "1000 Milwaukee Avenue, Glenview, IL 60025",
                    salesRepOwnerId,
                    tenantId).Value,

                Partner.Create(
                    Guid.Parse("b0000000-0000-0000-0000-000000000002"),
                    "Premium Materials Inc",
                    "Supplier",
                    "procurement@premmat.com",
                    "+1-972-555-0202",
                    "2600 Network Boulevard, Carrollton, TX 75007",
                    salesRepOwnerId,
                    tenantId).Value,

                Partner.Create(
                    Guid.Parse("b0000000-0000-0000-0000-000000000003"),
                    "Global Components Corp",
                    "Supplier",
                    "orders@globalcomponents.com",
                    "+1-408-555-0303",
                    "100 North First Street, San Jose, CA 95112",
                    salesRepOwnerId,
                    tenantId).Value,

                Partner.Create(
                    Guid.Parse("b0000000-0000-0000-0000-000000000004"),
                    "Industrial Parts Warehouse",
                    "Supplier",
                    "sales@industrialparts.com",
                    "+1-312-555-0404",
                    "500 West Madison Street, Chicago, IL 60661",
                    salesRepOwnerId,
                    tenantId).Value,
            };

            context.Partners.AddRange(customers);
            context.Partners.AddRange(suppliers);
            await context.CommitAsync();

            logger.LogInformation("Enhanced Partners module seeding completed:");
            logger.LogInformation("  Customers: {CustomerCount} with assigned sales rep", customers.Length);
            logger.LogInformation("  Suppliers: {SupplierCount} for purchasing scenarios", suppliers.Length);
            logger.LogInformation("  Data scope: Partners filtered by OwnerId (Alice Sales Rep)");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding enhanced Partners module");
            throw;
        }
    }
}
