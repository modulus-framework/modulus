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
            await context.SaveChangesAsync();

            logger.LogInformation("Partners module seeding completed: {PartnerCount} partners added", partners.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Partners module");
            throw;
        }
    }
}
