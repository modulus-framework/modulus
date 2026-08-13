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
            await context.CommitAsync();

            logger.LogInformation("Purchasing module seeding completed: {RequisitionCount} requisitions added",
                requisitions.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding Purchasing module");
            throw;
        }
    }

    public static async Task SeedEnhancedAsync(
        PurchasingDbContext context,
        ILogger logger,
        Guid tenantId,
        Guid buyerUserId,
        Guid purchasingManagerUserId,
        Guid northRegionOrgId)
    {
        try
        {
            if (await context.Requisitions.AnyAsync())
                return;

            // Supplier IDs from Partners module
            var techSuppliesSupplierId = Guid.Parse("supp0001-0000-0000-0000-000000000001");
            var premiumMaterialsSupplierId = Guid.Parse("supp0002-0000-0000-0000-000000000002");

            // Product IDs from Catalog module
            var widgetAProductId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var premiumWidgetProductId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var universalGadgetProductId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

            var requisitions = new List<PurchaseRequisition>();

            // Create requisitions for SoD and delegation scenarios
            for (int i = 1; i <= 4; i++)
            {
                var requisition = PurchaseRequisition.Create(
                    Guid.NewGuid(),
                    $"PR-{DateTime.UtcNow.Year}-{i:D5}",
                    buyerUserId, // Created by buyer Diana
                    northRegionOrgId,
                    tenantId).Value;

                // Add realistic requisition lines
                requisition.AddLine(widgetAProductId, "Widget A", 100, 100.00m);

                if (i <= 2)
                {
                    requisition.AddLine(premiumWidgetProductId, "Premium Widget", 50, 175.00m);
                }

                if (i <= 1)
                {
                    requisition.AddLine(universalGadgetProductId, "Universal Gadget", 25, 120.00m);
                }

                requisition.Submit(); // Submit for approval

                // Alternate between approved and awaiting approval
                if (i % 2 == 0)
                {
                    requisition.Approve(purchasingManagerUserId); // Approved by manager Eve
                }

                requisitions.Add(requisition);
            }

            // Create one requisition that will trigger SoD violation (same user as requester and approver)
            var sodRequisition = PurchaseRequisition.Create(
                Guid.NewGuid(),
                $"PR-{DateTime.UtcNow.Year}-SOD001",
                buyerUserId, // Diana created it
                northRegionOrgId,
                tenantId).Value;

            sodRequisition.AddLine(widgetAProductId, "Widget A", 10, 100.00m);
            sodRequisition.Submit();
            requisitions.Add(sodRequisition);

            context.Requisitions.AddRange(requisitions);
            await context.CommitAsync();

            logger.LogInformation("Enhanced Purchasing module seeding completed:");
            logger.LogInformation("  Requisitions: {RequisitionCount} requisitions with different approval states", requisitions.Count);
            logger.LogInformation("  SoD Setup: PR-2026-SOD001 for requester self-approval violation demo");
            logger.LogInformation("  Delegation Setup: Requisitions awaiting manager approval for delegation demo");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding enhanced Purchasing module");
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
