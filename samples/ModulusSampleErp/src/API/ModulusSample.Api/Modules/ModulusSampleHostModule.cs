using ModulusSample.Modules.Identity.Infrastructure;
using ModulusSample.Modules.Settings.Infrastructure;
using ModulusSample.Modules.Tenants.Infrastructure;
using ModulusSample.Modules.Features.Infrastructure;
using ModulusSample.Modules.VirtualFileExplorer.Infrastructure;
using ModulusSample.Modules.Notifications.Infrastructure;
using ModulusSample.Modules.Media.Infrastructure;
using ModulusSample.Modules.Catalog.Infrastructure;
using ModulusSample.Modules.Partners.Infrastructure;
using ModulusSample.Modules.Inventory.Infrastructure;
using ModulusSample.Modules.Sales.Infrastructure;
using ModulusSample.Modules.Purchasing.Infrastructure;
using Modulus.Core.Abstractions;
namespace ModulusSample.Api.Modules;

/// <summary>
/// Root startup module. Lists every business module via [DependsOn] so
/// Modulus auto-discovers the full graph via <c>AddModulus&lt;ModulusSampleHostModule&gt;</c>.
/// </summary>
[DependsOn(
    typeof(CatalogModule),
    typeof(PartnersModule),
    typeof(InventoryModule),
    typeof(SalesModule),
    typeof(PurchasingModule),
    typeof(IdentityModule),
    typeof(SettingsModule),
    typeof(TenantsModule),
    typeof(FeaturesModule),
    typeof(VirtualFileExplorerModule),
    typeof(NotificationsModule),
    typeof(MediaModule))]
public sealed class ModulusSampleHostModule : ModulusModule
{
}
