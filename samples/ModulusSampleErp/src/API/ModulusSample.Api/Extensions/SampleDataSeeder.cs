using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModulusSample.Modules.Features.Domain.Entities;
using ModulusSample.Modules.Features.Domain.ValueObjects;
using ModulusSample.Modules.Features.Infrastructure.Database;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Modules.Identity.Infrastructure.Database;
using ModulusSample.Modules.Media.Domain.Entities;
using ModulusSample.Modules.Media.Infrastructure.Database;
using ModulusSample.Modules.Notifications.Domain.Entities;
using ModulusSample.Modules.Notifications.Domain.ValueObjects;
using ModulusSample.Modules.Notifications.Infrastructure.Database;
using ModulusSample.Modules.Settings.Domain.Entities;
using ModulusSample.Modules.Settings.Domain.ValueObjects;
using ModulusSample.Modules.Settings.Infrastructure.Database;
using ModulusSample.Modules.Tenants.Domain.Entities;
using ModulusSample.Modules.Tenants.Domain.ValueObjects;
using ModulusSample.Modules.Tenants.Infrastructure.Database;
using ModulusSample.Modules.VirtualFileExplorer.Domain.Entities;
using ModulusSample.Modules.VirtualFileExplorer.Domain.ValueObjects;
using ModulusSample.Modules.VirtualFileExplorer.Infrastructure.Database;
using ModulusSample.Modules.Catalog.Infrastructure.Database;
using ModulusSample.Modules.Partners.Infrastructure.Database;
using ModulusSample.Modules.Inventory.Infrastructure.Database;
using ModulusSample.Modules.Sales.Infrastructure.Database;
using ModulusSample.Modules.Purchasing.Infrastructure.Database;
using ModulusSample.Modules.Billing.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ModulusSample.Api.Extensions;

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
        await SeedVirtualFoldersAsync(scope, logger);
        await SeedNotificationsAsync(scope, logger);
        await SeedMediaFoldersAsync(scope, logger);

        // Seed business modules (Catalog, Partners, Inventory, Sales, Purchasing, Billing)
        var catalogContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var partnersContext = scope.ServiceProvider.GetRequiredService<PartnersDbContext>();
        var inventoryContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var salesContext = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var purchasingContext = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
        var billingContext = scope.ServiceProvider.GetRequiredService<BillingDbContext>();

        // Demo Tenants with different plans
        var acmeCorpTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111"); // Enterprise plan
        var startupIncTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222"); // Starter plan

        // Org Hierarchy: Acme Corp -> 2 Regions -> 4 Branches/Warehouses
        var acmeCompanyId = Guid.Parse("aaaa0000-0000-0000-0000-000000000001");
        var acmeNorthRegionId = Guid.Parse("aaaa0000-0000-0000-0000-000000000011");
        var acmeSouthRegionId = Guid.Parse("aaaa0000-0000-0000-0000-000000000012");
        var acmeNycWarehouseId = Guid.Parse("aaaa0000-0000-0000-0000-000000000101"); // North Region
        var acmeBostonWarehouseId = Guid.Parse("aaaa0000-0000-0000-0000-000000000102"); // North Region
        var acmeMiamiWarehouseId = Guid.Parse("aaaa0000-0000-0000-0000-000000000201"); // South Region
        var acmeAtlantaWarehouseId = Guid.Parse("aaaa0000-0000-0000-0000-000000000202"); // South Region

        // 6 Personas: Sales Rep, Branch Manager, Regional Manager, Buyer, Purchasing Manager, Finance
        var aliceSalesRepId = Guid.Parse("alice000-0000-0000-0000-000000000001");
        var bobBranchMgrId = Guid.Parse("bob00000-0000-0000-0000-000000000001");
        var charlieRegionalMgrId = Guid.Parse("charlie0-0000-0000-0000-000000000001");
        var dianaBuyerId = Guid.Parse("diana000-0000-0000-0000-000000000001");
        var evePurchasingMgrId = Guid.Parse("eve00000-0000-0000-0000-000000000001");
        var frankFinanceId = Guid.Parse("frank000-0000-0000-0000-000000000001");

        logger.LogInformation("Seeding comprehensive demo data for scenarios");

        // Seed modules with detailed demo data
        await CatalogDbContextSeed.SeedEnhancedAsync(catalogContext, logger, acmeCorpTenantId);
        await PartnersDbContextSeed.SeedEnhancedAsync(partnersContext, logger, acmeCorpTenantId, aliceSalesRepId);
        await InventoryDbContextSeed.SeedEnhancedAsync(inventoryContext, logger, acmeCorpTenantId,
            acmeNycWarehouseId, acmeBostonWarehouseId, acmeMiamiWarehouseId, acmeAtlantaWarehouseId);
        await SalesDbContextSeed.SeedEnhancedAsync(salesContext, logger, acmeCorpTenantId, aliceSalesRepId, acmeNycWarehouseId);
        await PurchasingDbContextSeed.SeedEnhancedAsync(purchasingContext, logger, acmeCorpTenantId,
            dianaBuyerId, evePurchasingMgrId, acmeNorthRegionId);
        await BillingDbContextSeed.SeedEnhancedAsync(billingContext, logger, acmeCorpTenantId, acmeCompanyId, frankFinanceId);

        logger.LogInformation("Comprehensive demo data seeded:");
        logger.LogInformation("  Tenants: Acme Corp[Enterprise], StartUp Inc[Starter]");
        logger.LogInformation("  Org Hierarchy: Acme -> North/South Regions -> NYC/Boston/Miami/Atlanta Warehouses");
        logger.LogInformation("  6 Personas: Alice[Sales Rep], Bob[Branch Mgr], Charlie[Regional Mgr], Diana[Buyer], Eve[Purchasing Mgr], Frank[Finance]");
        logger.LogInformation("  Business Data: 10 products, 4 customers, 4 suppliers, 4 warehouses with stock, 5 orders");
    }

    private static async Task SeedSettingsAsync(IServiceScope scope, ILogger logger)
    {
        var context = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        if (await context.Settings.AnyAsync())
        {
            return;
        }

        var settings = new[]
        {
            ("app.name", "Modulus Sample ERP", "General", "Application display name", true),
            ("app.default-locale", "en-US", "General", "Default UI locale", true),
            ("company.support-email", "support@modulussample.com", "Company", "Public support contact", true),
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

        var connectionString = "Host=localhost;Port=5432;Database=ModulusSample;Username=ModulusSample;Password=ModulusSample";

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
        var context = scope.ServiceProvider.GetRequiredService<FeaturesDbContext>();
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

    private static async Task SeedVirtualFoldersAsync(IServiceScope scope, ILogger logger)
    {
        var context = scope.ServiceProvider.GetRequiredService<VirtualFileExplorerDbContext>();
        if (await context.VirtualFolders.AnyAsync())
        {
            return;
        }

        foreach (string folderName in new[] { "Contracts", "Invoices", "Product-Photos" })
        {
            var result = VirtualFolder.Create(
                VirtualFolderId.Create(),
                folderName,
                null,
                GlobalTenantId,
                SystemUser);

            if (result.IsSuccess)
            {
                context.VirtualFolders.Add(result.Value);
            }
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded VirtualFileExplorer module sample data");
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
            ("Welcome to Modulus Sample ERP", "Your workspace is ready. Explore the Settings, Features, Tenants and VirtualFileExplorer modules.", NotificationType.Success),
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

    private static async Task SeedMediaFoldersAsync(IServiceScope scope, ILogger logger)
    {
        var context = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        if (await context.MediaFolders.AnyAsync())
        {
            return;
        }

        var productId = Guid.NewGuid();
        context.MediaFolders.Add(new MediaFolder(
            productId,
            "Products",
            "Product imagery",
            null,
            "root/Products",
            null,
            null));

        context.MediaFolders.Add(new MediaFolder(
            Guid.NewGuid(),
            "Marketing",
            "Campaign and brand assets",
            null,
            "root/Marketing",
            null,
            null));

        context.MediaFolders.Add(new MediaFolder(
            Guid.NewGuid(),
            "Product A",
            "Assets for a specific product",
            productId,
            "root/Products/Product A",
            null,
            null));

        await context.SaveChangesAsync();
        logger.LogInformation("Seeded Media module sample data");
    }
}
