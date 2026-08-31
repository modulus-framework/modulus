using TradeFlow.Modules.Identity.Infrastructure;
using TradeFlow.Modules.Configuration.Infrastructure;
using TradeFlow.Modules.Tenants.Infrastructure;
using TradeFlow.Modules.Notifications.Infrastructure;
using TradeFlow.Modules.Vendors.Infrastructure;
using TradeFlow.Modules.OrgStructure.Infrastructure;
using TradeFlow.Modules.Budgeting.Infrastructure;
using TradeFlow.Modules.Customs.Infrastructure;
using TradeFlow.Modules.Procurement.Infrastructure;
using TradeFlow.Modules.TradeFinance.Infrastructure;
using TradeFlow.Modules.Import.Infrastructure;
using TradeFlow.Modules.Inventory.Infrastructure;
using TradeFlow.Modules.Costing.Infrastructure;
using TradeFlow.Modules.Finance.Infrastructure;
using TradeFlow.Modules.VirtualFileExplorer.Infrastructure;
using TradeFlow.Modules.WorkflowEngine.Infrastructure;
using Modulus.Core.Abstractions;
namespace TradeFlow.Api.Modules;

/// <summary>
/// Root startup module. Lists every module via [DependsOn] so
/// Modulus auto-discovers the full graph via <c>AddModulus&lt;TradeFlowHostModule&gt;</c>.
/// </summary>
[DependsOn(
    typeof(IdentityModule),
    typeof(ConfigurationModule),
    typeof(TenantsModule),
    typeof(NotificationsModule),
    typeof(VendorsModule),
    typeof(OrgStructureModule),
    typeof(BudgetsModule),
    typeof(CustomsModule),
    typeof(ProcurementModule),
    typeof(TradeFinanceModule),
    typeof(ImportModule),
    typeof(InventoryModule),
    typeof(CostingModule),
    typeof(FinanceModule),
    typeof(VirtualFileExplorerModule),
    typeof(WorkflowEngineModule))]
public sealed class TradeFlowHostModule : ModulusModule
{
}
