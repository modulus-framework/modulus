using ProcureFlow.Modules.Identity.Infrastructure;
using ProcureFlow.Modules.Configuration.Infrastructure;
using ProcureFlow.Modules.Tenants.Infrastructure;
using ProcureFlow.Modules.Notifications.Infrastructure;
using ProcureFlow.Modules.Vendors.Infrastructure;
using ProcureFlow.Modules.OrgStructure.Infrastructure;
using ProcureFlow.Modules.Budgeting.Infrastructure;
using ProcureFlow.Modules.Customs.Infrastructure;
using ProcureFlow.Modules.Procurement.Infrastructure;
using ProcureFlow.Modules.TradeFinance.Infrastructure;
using ProcureFlow.Modules.Import.Infrastructure;
using ProcureFlow.Modules.Inventory.Infrastructure;
using ProcureFlow.Modules.Costing.Infrastructure;
using ProcureFlow.Modules.Finance.Infrastructure;
using ProcureFlow.Modules.VirtualFileExplorer.Infrastructure;
using ProcureFlow.Modules.WorkflowEngine.Infrastructure;
using Modulus.Core.Abstractions;
namespace ProcureFlow.Api.Modules;

/// <summary>
/// Root startup module. Lists every module via [DependsOn] so
/// Modulus auto-discovers the full graph via <c>AddModulus&lt;ProcureFlowHostModule&gt;</c>.
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
public sealed class ProcureFlowHostModule : ModulusModule
{
}
