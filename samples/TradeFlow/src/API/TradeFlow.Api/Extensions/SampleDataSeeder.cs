using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using TradeFlow.Modules.Configuration.Domain.Entities;
using TradeFlow.Modules.Configuration.Domain.ValueObjects;
using TradeFlow.Modules.Configuration.Infrastructure.Database;
using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Modules.Identity.Infrastructure.Database;
using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Modules.Notifications.Infrastructure.Database;
using TradeFlow.Modules.Tenants.Domain.Entities;
using TradeFlow.Modules.Tenants.Domain.ValueObjects;
using TradeFlow.Modules.Tenants.Infrastructure.Database;
using TradeFlow.Modules.Vendors.Infrastructure.Database;
using TradeFlow.Modules.Vendors.Domain.Entities;
using TradeFlow.Modules.Budgeting.Infrastructure.Database;
using TradeFlow.Modules.Budgeting.Domain.Entities;
using TradeFlow.Modules.Procurement.Infrastructure.Database;
using TradeFlow.Modules.Procurement.Domain.Entities;
using TradeFlow.Modules.Import.Infrastructure.Database;
using TradeFlow.Modules.Import.Domain.Entities;
using TradeFlow.Modules.Finance.Infrastructure.Database;
using TradeFlow.Modules.Finance.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TradeFlow.Api.Extensions;

/// <summary>
/// Seeds lightweight sample data for every business module so the API is
/// exercisable out of the box (GET endpoints return records, not empty lists).
/// All records use TenantId = Guid.Empty (the "no tenant header" scope) so the
/// default API flow without an X-Tenant-Id header can read them.
/// </summary>
internal static class SampleDataSeeder
{
    private static readonly Guid GlobalTenantId = Guid.Empty;
    private const string SystemUser = "system";

    public static async Task SeedAsync(IServiceScope scope)
    {
        ILogger logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        await SeedSettingsAsync(scope, logger);
        await SeedTenantsAsync(scope, logger);
        await SeedFeatureFlagsAsync(scope, logger);
        await SeedNotificationsAsync(scope, logger);
        await SeedBusinessModulesAsync(scope, logger);

        logger.LogInformation("Sample data seeding completed");
    }

    private static async Task SeedSettingsAsync(IServiceScope scope, ILogger logger)
    {
        var context = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
        if (await context.Settings.AnyAsync())
        {
            return;
        }

        var settings = new[]
        {
            ("app.name", "TradeFlow", "General", "Application display name", true),
            ("app.default-locale", "en-US", "General", "Default UI locale", true),
            ("company.support-email", "support@TradeFlow.com", "Company", "Public support contact", true),
            ("notifications.email.enabled", "true", "Notifications", "Whether transactional email is sent", false),
        };

        foreach ((string key, string value, string category, string description, bool isPublic) in settings)
        {
            var result = Setting.Create(
                SettingId.Create(),
                SettingKey.FromString(key),
                value,
                category,
                description,
                isPublic,
                GlobalTenantId,
                SystemUser);

            if (result.IsSuccess)
            {
                context.Settings.Add(result.Value);
            }
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded Settings module sample data");
    }

    private static async Task SeedTenantsAsync(IServiceScope scope, ILogger logger)
    {
        var context = scope.ServiceProvider.GetRequiredService<TenantsDbContext>();
        if (await context.Tenants.AnyAsync())
        {
            return;
        }

        var connectionString = "Host=localhost;Port=5432;Database=TradeFlow;Username=TradeFlow;Password=TradeFlow";

        var tenants = new[]
        {
            (
                Name: "Acme Corporation",
                Subdomain: "acme",
                Plan: "Enterprise",
                TenantId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Features: new[] { "multi-warehouse-transfers", "billing-reports", "ar-aging", "advanced-analytics" }
            ),
            (
                Name: "StartUp Inc",
                Subdomain: "startup",
                Plan: "Starter",
                TenantId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Features: new[] { "basic-reports" }
            )
        };

        foreach ((string name, string subdomain, string plan, Guid tenantId, string[] features) in tenants)
        {
            var subdomainResult = Subdomain.Create(subdomain);
            if (!subdomainResult.IsSuccess)
            {
                continue;
            }

            JsonDocument metadata = JsonDocument.Parse(new JsonObject
            {
                ["plan"] = plan,
                ["features"] = JsonSerializer.SerializeToNode(features)
            }.ToJsonString());

            var result = Tenant.Create(
                TenantId.From(tenantId),
                name,
                subdomainResult.Value,
                connectionString,
                metadata,
                JsonDocument.Parse("{}"),
                SystemUser);

            if (result.IsSuccess)
            {
                context.Tenants.Add(result.Value);
            }
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded Tenants: Acme Corp[Enterprise], StartUp Inc[Starter]");
    }

    private static async Task SeedFeatureFlagsAsync(IServiceScope scope, ILogger logger)
    {
        var context = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
        if (await context.FeatureFlags.AnyAsync())
        {
            return;
        }

        var flags = new[]
        {
            ("catalog.new-checkout", "New checkout experience", true),
            ("catalog.promo-banners", "Promotional banners on storefront", true),
            ("import.export", "Bulk import/export", false),
        };

        foreach ((string key, string name, bool enabled) in flags)
        {
            var result = FeatureFlag.Create(
                FeatureFlagId.Create(),
                FeatureKey.FromString(key),
                name,
                "Seeded sample feature flag",
                enabled,
                GlobalTenantId,
                SystemUser);

            if (result.IsSuccess)
            {
                context.FeatureFlags.Add(result.Value);
            }
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded Features module sample data");
    }

    private static async Task SeedNotificationsAsync(IServiceScope scope, ILogger logger)
    {
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        if (await context.Notifications.AnyAsync())
        {
            return;
        }

        var adminUser = await identity.Users
            .Where(u => u.UserName == UserName.Create("admin"))
            .FirstOrDefaultAsync();

        if (adminUser is null)
        {
            logger.LogWarning("SampleDataSeeder: admin user not found, skipping notifications seed");
            return;
        }

        var notifications = new[]
        {
            ("Welcome to TradeFlow", "Your workspace is ready. Explore the Settings, Features, Tenants and Media modules.", NotificationType.Success),
            ("Sample data loaded", "A few sample records were seeded so you can exercise every module's endpoints.", NotificationType.Info),
        };

        foreach ((string title, string message, NotificationType type) in notifications)
        {
            var result = Notification.Create(
                NotificationId.Create(),
                adminUser.Id.Value,
                title,
                message,
                type,
                GlobalTenantId,
                SystemUser);

            if (result.IsSuccess)
            {
                context.Notifications.Add(result.Value);
            }
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded Notifications module sample data");
    }

    private static async Task SeedBusinessModulesAsync(IServiceScope scope, ILogger logger)
    {
        await SeedVendorsAsync(scope, logger);
        await SeedBudgetsAsync(scope, logger);
        await SeedProcurementAsync(scope, logger);
        await SeedImportAsync(scope, logger);
        await SeedFinanceAsync(scope, logger);
    }

    private static async Task SeedVendorsAsync(IServiceScope scope, ILogger logger)
    {
        var context = scope.ServiceProvider.GetRequiredService<VendorsDbContext>();
        if (await context.Vendors.AnyAsync())
        {
            return;
        }

        var vendors = new[]
        {
            ("Acme Supplies Ltd", "Acme Supplies Limited", "US", VendorType.Manufacturer),
            ("Global Trade Corp", "Global Trade Corporation", "CN", VendorType.Trader)
        };

        foreach ((string name, string legalName, string country, VendorType type) in vendors)
        {
            var vendor = Vendor.Create(
                Guid.NewGuid(),
                GlobalTenantId,
                name,
                legalName,
                country,
                type,
                tin: "TIN-001",
                bin: "BIN-001",
                email: "contact@acme.example.com",
                phone: "+1-555-0100",
                address: "123 Commerce St",
                SystemUser);

            if (vendor.IsSuccess)
            {
                context.Vendors.Add(vendor.Value);
            }
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded Vendors module sample data");
    }

    private static async Task SeedBudgetsAsync(IServiceScope scope, ILogger logger)
    {
        var context = scope.ServiceProvider.GetRequiredService<BudgetsDbContext>();
        if (await context.Budgets.AnyAsync())
        {
            return;
        }

        var budget = Budget.Create(
            Guid.NewGuid(),
            GlobalTenantId,
            2026,
            Guid.NewGuid(),
            "IT Equipment",
            projectId: null,
            "USD",
            500000m,
            BudgetBlockMode.Soft,
            Guid.NewGuid(),
            SystemUser);

        if (budget.IsSuccess)
        {
            context.Budgets.Add(budget.Value);
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded Budgets module sample data");
    }

    private static async Task SeedProcurementAsync(IServiceScope scope, ILogger logger)
    {
        var context = scope.ServiceProvider.GetRequiredService<ProcurementDbContext>();
        if (await context.PurchaseRequisitions.AnyAsync())
        {
            return;
        }

        var pr = PurchaseRequisition.Create(GlobalTenantId, "PR-2026-001", "John Doe");
        var vendorId = await scope.ServiceProvider.GetRequiredService<VendorsDbContext>()
            .Vendors.Select(v => v.Id).FirstAsync();

        var prLine = new PrLine(
            Guid.NewGuid(),
            itemId: Guid.NewGuid(),
            freeText: "Laptop Computer",
            category: "IT",
            quantity: 10,
            uom: "PCS",
            needByDate: new DateOnly(2026, 8, 15),
            suggestedVendorId: vendorId,
            estimatedUnitPrice: 1500m,
            currency: "USD",
            notes: "Standard laptop configuration");
        pr.AddLine(prLine);
        pr.Submit(0);

        context.PurchaseRequisitions.Add(pr);

        var po = PurchaseOrder.Create(
            GlobalTenantId,
            "PO-2026-001",
            PoSource.PrDirect,
            vendorId,
            "USD",
            "CIF",
            PaymentMode.Tt,
            latestShipmentDate: new DateOnly(2026, 8, 20),
            partialShipmentAllowed: true,
            transshipmentAllowed: false,
            psiRequired: true,
            SystemUser);

        var poLine = new PoLine(
            Guid.NewGuid(),
            itemId: prLine.ItemId,
            freeText: prLine.FreeText,
            hsCode: "8471.30",
            quantity: prLine.Quantity,
            uom: prLine.Uom,
            unitPrice: 1500m,
            receivedQuantity: 0m,
            notes: prLine.Notes);
        po.AddLine(poLine);

        var feasibilitySnapshot = new FeasibilitySnapshot(
            score: 85m,
            verdict: "Approved",
            reasons: Array.Empty<string>(),
            evaluatedAtUtc: DateTime.UtcNow);
        po.Submit(feasibilitySnapshot, requiresCfoOverride: false);

        context.PurchaseOrders.Add(po);

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded Procurement module sample data");
    }

    private static async Task SeedImportAsync(IServiceScope scope, ILogger logger)
    {
        var context = scope.ServiceProvider.GetRequiredService<ImportDbContext>();
        if (await context.ImportFiles.AnyAsync())
        {
            return;
        }

        var poId = await scope.ServiceProvider.GetRequiredService<ProcurementDbContext>()
            .PurchaseOrders.Select(po => po.Id).FirstAsync();

        var importFile = ImportFile.Create(
            GlobalTenantId,
            Guid.NewGuid(),
            2026,
            1,
            poId,
            "CIF",
            "USD",
            "Shanghai",
            "Chittagong",
            15000m,
            SystemUser);

        context.ImportFiles.Add(importFile);

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded Import module sample data");
    }

    private static async Task SeedFinanceAsync(IServiceScope scope, ILogger logger)
    {
        var context = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        if (await context.CostCenters.AnyAsync())
        {
            return;
        }

        var vendorId = await scope.ServiceProvider.GetRequiredService<VendorsDbContext>()
            .Vendors.Select(v => v.Id).FirstAsync();

        var itCostCenter = new CostCenter(
            Guid.NewGuid(), GlobalTenantId, "IT", "Information Technology", null, true);
        var opsCostCenter = new CostCenter(
            Guid.NewGuid(), GlobalTenantId, "OPS", "Operations", null, true);
        context.CostCenters.Add(itCostCenter);
        context.CostCenters.Add(opsCostCenter);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        context.FxRates.Add(new FxRate(
            Guid.NewGuid(), GlobalTenantId, today, "USD", "BDT", 110m, FxSource.BangladeshBank,
            "BBL-2026-001", DateTime.UtcNow));
        context.FxRates.Add(new FxRate(
            Guid.NewGuid(), GlobalTenantId, today, "EUR", "BDT", 119m, FxSource.BangladeshBank,
            "BBL-2026-002", DateTime.UtcNow));

        var invoice = ApInvoice.Create(
            GlobalTenantId, "AP-2026-001", vendorId, today, today.AddDays(30),
            "USD", 15000m, ApInvoiceSource.Manual, isCreditNote: false, SystemUser);
        invoice.AddLine(new InvoiceLine(
            Guid.NewGuid(), Guid.NewGuid(), null, "Laptop Computers", 10, "PCS", 1500m, 15000m));
        invoice.Submit();
        invoice.Approve(SystemUser);
        context.ApInvoices.Add(invoice);

        var proposal = PaymentProposal.Create(
            GlobalTenantId, "PP-2026-001", today.AddDays(15), "USD", 15000m, SystemUser);
        proposal.AddInvoice(invoice.Id);
        proposal.Approve(SystemUser);
        context.PaymentProposals.Add(proposal);

        var journal = JournalBatch.Create(
            GlobalTenantId, "JB-2026-001", today, "AP accrual for invoice AP-2026-001", "USD", SystemUser);
        journal.AddLine(new JournalLine(
            Guid.NewGuid(), "5100", "Office Equipment Expense", "AP accrual", 15000m, 0m, itCostCenter.Id));
        journal.AddLine(new JournalLine(
            Guid.NewGuid(), "2000", "Accounts Payable", "AP accrual", 0m, 15000m, null));
        journal.Post(SystemUser);
        context.JournalBatches.Add(journal);

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded Finance module sample data");
    }
}
