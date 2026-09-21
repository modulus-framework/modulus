using System.Text.Json;

namespace Modulus.Cli.Services;

internal static class UiModuleCatalog
{
    private static readonly string CurrentVersion = FrameworkVersion.Current;

    public static IReadOnlyList<UiModuleDefinition> All { get; } =
    [
        new(
            "Modulus.UI.Core",
            "UI Core",
            CurrentVersion,
            "Cobytelabs.Modulus.UI.Core",
            "Modulus.UI",
            "AddModulusUi",
            [],
            [],
            ["Navigation", "Tabler shell", "HTMX", "Alpine"],
            IsCore: true),
        new(
            "Modulus.Identity",
            "Identity",
            CurrentVersion,
            "Cobytelabs.Modulus.UI.Identity",
            "Modulus.UI.Identity",
            "AddModulusIdentityUi",
            ["Modulus.UI.Core"],
            ["Cobytelabs.Modulus.Identity", "Cobytelabs.Modulus.Platform"],
            ["Login", "Register", "SignOut"]),
        new(
            "Modulus.Tenancy",
            "Tenancy",
            CurrentVersion,
            "Cobytelabs.Modulus.UI.Tenancy",
            "Modulus.UI.Tenancy",
            "AddModulusTenancyUi",
            ["Modulus.UI.Core"],
            ["Cobytelabs.Modulus.Platform"],
            ["Tenancy"],
            BackendRegistrations: [new("Modulus.MultiTenancy.Extensions", "AddMultiTenancy(", "builder.Services.AddMultiTenancy();")],
            Gate: new("TenancyUi", "tenancy:view")),
        new(
            "Modulus.Permissions",
            "Permissions",
            CurrentVersion,
            "Cobytelabs.Modulus.UI.Permissions",
            "Modulus.UI.Permissions",
            "AddModulusPermissionsUi",
            ["Modulus.UI.Core"],
            ["Cobytelabs.Modulus.Platform"],
            ["Permissions"],
            BackendRegistrations: [new("Modulus.Authorization.Extensions", "AddModulusAuthorization(", "builder.Services.AddModulusAuthorization();")],
            Gate: new("PermissionsUi", "permissions:view")),
        new(
            "Modulus.Settings",
            "Settings",
            CurrentVersion,
            "Cobytelabs.Modulus.UI.Settings",
            "Modulus.UI.Settings",
            "AddModulusSettingsUi",
            ["Modulus.UI.Core"],
            ["Cobytelabs.Modulus.Platform"],
            ["Settings"],
            BackendRegistrations: [new("Modulus.Settings", "AddModulusSettings(", "builder.Services.AddModulusSettings();")],
            Gate: new("SettingsUi", "settings:manage")),
        new(
            "Modulus.AuditLogging",
            "AuditLogging",
            CurrentVersion,
            "Cobytelabs.Modulus.UI.AuditLogging",
            "Modulus.UI.AuditLogging",
            "AddModulusAuditLoggingUi",
            ["Modulus.UI.Core"],
            ["Cobytelabs.Modulus.Platform"],
            ["AuditLogs"],
            BackendRegistrations: [new("Modulus.AuditLogging", "AddModulusAuditLogging(", "builder.Services.AddModulusAuditLogging();")],
            Gate: new("AuditLoggingUi", "audit:view")),
        new(
            "Modulus.Notifications",
            "Notifications",
            CurrentVersion,
            "Cobytelabs.Modulus.UI.Notifications",
            "Modulus.UI.Notifications",
            "AddModulusNotificationsUi",
            ["Modulus.UI.Core"],
            ["Cobytelabs.Modulus.Platform"],
            ["Notifications"],
            BackendRegistrations: [new("Modulus.Notifications", "AddModulusNotifications(", "builder.Services.AddModulusNotifications();")]),
        new(
            "Modulus.Files",
            "Files",
            CurrentVersion,
            "Cobytelabs.Modulus.UI.Files",
            "Modulus.UI.Files",
            "AddModulusFilesUi",
            ["Modulus.UI.Core"],
            ["Cobytelabs.Modulus.Platform"],
            ["Files"],
            BackendRegistrations: [new("Modulus.Storage", "FileStorage(", "builder.Services.AddFileStorage(builder.Configuration);")],
            Gate: new("FilesUi", "files:manage")),
        new(
            "Modulus.Users",
            "Users",
            CurrentVersion,
            "Cobytelabs.Modulus.UI.Users",
            "Modulus.UI.Users",
            "AddModulusUsersUi",
            ["Modulus.Identity", "Modulus.UI.Core"],
            ["Cobytelabs.Modulus.Identity", "Cobytelabs.Modulus.Platform"],
            ["Users", "Roles"],
            Gate: new("UsersUi", "users:manage")),
        new(
            "Modulus.Theme.Tabler",
            "Tabler",
            CurrentVersion,
            "Cobytelabs.Modulus.UI.Theme.Tabler",
            "Modulus.UI.Theme.Tabler",
            "AddTablerTheme",
            ["Modulus.UI.Core"],
            [],
            ["Application/Account/Empty/Public layouts", "Design tokens", "Vendored htmx + Alpine (CSP)", "Error pages"],
            HasEndpoints: false,
            ExtensionNamespace: "Modulus.UI.Theming.Tabler"),
    ];

    public static UiModuleDefinition Find(string idOrPackageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrPackageId);
        var query = idOrPackageId.Trim();
        var match = All.FirstOrDefault(module =>
            EqualsIgnoreCase(module.Id, query)
            || EqualsIgnoreCase(module.Name, query)
            || EqualsIgnoreCase(module.PackageId, query)
            || EqualsIgnoreCase(module.Namespace, query)
            || EqualsIgnoreCase(PackageSuffix(module.PackageId), query));

        if (match is null)
            throw new InvalidOperationException(
                $"Unknown UI module '{idOrPackageId}'. Run 'modulus ui list' to see available modules.");

        return match;
    }

    public static IReadOnlyList<UiModuleDefinition> Search(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var text = query.Trim();
        return All
            .Where(module =>
                Contains(module.Id, text)
                || Contains(module.Name, text)
                || Contains(module.PackageId, text)
                || Contains(module.Namespace, text)
                || module.Features.Any(feature => Contains(feature, text)))
            .ToArray();
    }

    public static IReadOnlyList<UiModuleDefinition> ResolveInstallOrder(string idOrPackageId)
    {
        var target = Find(idOrPackageId);
        var result = new List<UiModuleDefinition>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Visit(target);
        return result;

        void Visit(UiModuleDefinition module)
        {
            if (result.Contains(module))
                return;

            if (!visiting.Add(module.Id))
                throw new InvalidOperationException(
                    $"Circular UI module dependency detected at '{module.Id}'.");

            foreach (var dependencyId in module.Dependencies)
                Visit(Find(dependencyId));

            visiting.Remove(module.Id);
            result.Add(module);
        }
    }

    public static string PackageIdFor(string moduleId)
        => Find(moduleId).PackageId;

    public static bool IsCore(string packageId)
        => EqualsIgnoreCase(packageId, "Cobytelabs.Modulus.UI.Core");

    private static bool EqualsIgnoreCase(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string value, string query)
        => value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string PackageSuffix(string packageId)
    {
        const string prefix = "Cobytelabs.Modulus.UI.";
        return packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? packageId[prefix.Length..]
            : packageId;
    }
}

internal sealed record UiModuleDefinition(
    string Id,
    string Name,
    string Version,
    string PackageId,
    string Namespace,
    string AddMethod,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> BackendPackageIds,
    IReadOnlyList<string> Features,
    bool IsCore = false,
    bool HasEndpoints = true,
    string? ExtensionNamespace = null,
    IReadOnlyList<UiBackendRegistration>? BackendRegistrations = null,
    UiAccessGate? Gate = null)
{
    public IReadOnlyList<string> DependencyPackageIds => Dependencies
        .Select(UiModuleCatalog.PackageIdFor)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public string AddCall => IsCore
        ? "builder.Services.AddModulusUi();"
        : $"builder.Services.{AddMethod}(builder.Configuration);";
}

/// <summary>
/// A backend service registration a feature UI needs before its pages resolve (e.g. Files needs
/// <c>IFileStorage</c>). <paramref name="Marker"/> is what to look for in <c>Program.cs</c> to see the
/// registration is already there, including a hand-written or provider-specific one, which is left alone.
/// Include the opening parenthesis: <c>AddModulusSettings</c> alone also matches <c>AddModulusSettingsUi(</c>.
/// </summary>
internal sealed record UiBackendRegistration(string Namespace, string Marker, string Call);

/// <summary>
/// How a generated web app locks a feature UI to administrators: <paramref name="Section"/> is the UI's options section
/// (<c>UsersUi</c>), whose <c>RequirePermission</c> is set to <paramref name="Permission"/>, and the Admin role is granted it.
/// See <see cref="UiAccessGates"/>.
/// </summary>
internal sealed record UiAccessGate(string Section, string Permission);
