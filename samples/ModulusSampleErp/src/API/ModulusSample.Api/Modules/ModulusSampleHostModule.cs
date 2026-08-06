using ModulusSample.Modules.Identity.Infrastructure;
using ModulusSample.Modules.Settings.Infrastructure;
using ModulusSample.Modules.Tenants.Infrastructure;
using ModulusSample.Modules.Features.Infrastructure;
using ModulusSample.Modules.VirtualFileExplorer.Infrastructure;
using ModulusSample.Modules.Notifications.Infrastructure;
using Modulus.Core.Abstractions;

namespace ModulusSample.Api.Modules;

/// <summary>
/// Root startup module. Lists every business module via [DependsOn] so
/// Modulus auto-discovers the full graph via <c>AddModulus&lt;ModulusSampleHostModule&gt;</c>.
/// </summary>
[DependsOn(
    typeof(IdentityModule),
    typeof(SettingsModule),
    typeof(TenantsModule),
    typeof(FeaturesModule),
    typeof(VirtualFileExplorerModule),
    typeof(NotificationsModule))]
public sealed class ModulusSampleHostModule : ModulusModule
{
}
