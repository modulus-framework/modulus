using Modulus.Core.Abstractions;

using ProcureFlow.Modules.Identity.Infrastructure;
using ProcureFlow.Modules.TenantManagement.Infrastructure;
using ProcureFlow.Modules.Organization.Infrastructure;
using ProcureFlow.Modules.Catalog.Infrastructure;
using ProcureFlow.Modules.SupplierManagement.Infrastructure;
using ProcureFlow.Modules.Procurement.Infrastructure;
using ProcureFlow.Modules.Approval.Infrastructure;
using ProcureFlow.Modules.Warehouse.Infrastructure;
using ProcureFlow.Modules.Inventory.Infrastructure;
using ProcureFlow.Modules.Finance.Infrastructure;
using ProcureFlow.Modules.Payment.Infrastructure;
using ProcureFlow.Modules.Notification.Infrastructure;
using ProcureFlow.Modules.Audit.Infrastructure;
using ProcureFlow.Modules.Reporting.Infrastructure;
namespace ProcureFlow.Api.Modules;

/// <summary>
/// Root startup module for ProcureFlow.  Lists every business module
/// via [DependsOn] so that Modulus auto-discovers the full graph.
/// </summary>
[DependsOn(typeof(ReportingModule))]
[DependsOn(typeof(AuditModule))]
[DependsOn(typeof(NotificationModule))]
[DependsOn(typeof(PaymentModule))]
[DependsOn(typeof(FinanceModule))]
[DependsOn(typeof(InventoryModule))]
[DependsOn(typeof(WarehouseModule))]
[DependsOn(typeof(ApprovalModule))]
[DependsOn(typeof(ProcurementModule))]
[DependsOn(typeof(SupplierManagementModule))]
[DependsOn(typeof(CatalogModule))]
[DependsOn(typeof(OrganizationModule))]
[DependsOn(typeof(TenantManagementModule))]
[DependsOn(typeof(IdentityModule))]
public sealed class ProcureFlowHostModule : ModulusModule
{
}
