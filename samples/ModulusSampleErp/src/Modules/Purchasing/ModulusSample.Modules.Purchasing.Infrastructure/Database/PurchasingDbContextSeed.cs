using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModulusSample.Modules.Purchasing.Domain.Entities;

namespace ModulusSample.Modules.Purchasing.Infrastructure.Database;

public static class PurchasingDbContextSeed
{
    public static async Task SeedAsync(
        PurchasingDbContext context,
        ILogger logger,
        Guid tenantId,
        Guid orgUnitId)
    {
        try
        {
            if (await context.Requisitions.AnyAsync())
                return;

            var requesterId = Guid.NewGuid();
            var approverId = Guid.NewGuid();

            var requisitions = new[]
            {
                // Create and populate a requisition
                CreateRequisition(Guid.NewGuid(), "REQ-2026-001", requesterId, orgUnitId, tenantId),
                CreateRequisition(Guid.NewGuid(), "REQ-2026-002", requesterId, orgUnitId, tenantId),
                CreateRequisition(Guid.NewGuid(), "REQ-2026-003", requesterId, orgUnitId, tenantId),
            };

            // Set some as submitted and approved to show workflow states
            requisitions[0].Submit();
            requisitions[0].Approve(approverId);

            requisitions[1].Submit();

            // Third one stays in Draft

            context.Requisitions.AddRange(requisitions);
            await context.SaveChangesAsync();

            logger.LogInformation("Purchasing module seeding completed: {RequisitionCount} requisitions added",
                requisitions.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Purchasing module");
            throw;
        }
    }

    private static PurchaseRequisition CreateRequisition(
        Guid id,
        string number,
        Guid requesterId,
        Guid orgUnitId,
        Guid tenantId)
    {
        var requisition = PurchaseRequisition.Create(id, number, requesterId, orgUnitId, tenantId).Value;

        // Add some sample lines
        requisition.AddLine(Guid.NewGuid(), "Raw Material A", 100m, 50m);
        requisition.AddLine(Guid.NewGuid(), "Raw Material B", 50m, 75m);

        return requisition;
    }
}
