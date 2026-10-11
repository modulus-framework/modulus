using System.Text.Json;

namespace Modulus.Cli.Services;

/// <summary>A page of the admin dashboard starter: the file stem the manifest uses, the <c>AdminPages</c> flag its menu entry has, and its route.</summary>
internal sealed record UiAdminPage(string File, string Key, string Flag, string Route);

/// <summary>
/// <c>ui add-admin</c>: renders the <c>admin</c> feature of the <c>Modulus.Ui.Templates</c> manifest (a working, editable dashboard starter:
/// users, roles, tenants, notifications, audit log, settings, dashboards) into the app's UI project and wires the host
/// (<c>AddModulusAdminUi</c> for the menu, controller routing for MVC). The pages read their data through the services of
/// <c>Modulus.Ui.Abstractions.Admin</c>; until the app registers its own, sample data answers.
/// </summary>
internal static class UiAdminScaffold
{
    public const string Feature = "admin";

    public static readonly IReadOnlyList<UiAdminPage> Pages =
    [
        new("Dashboard", "dashboard", "Dashboard", "/dashboard"),
        new("Analytics", "analytics", "Analytics", "/analytics"),
        new("Reports", "reports", "Reports", "/reports"),
        new("Users", "users", "Users", "/admin/users"),
        new("Roles", "roles", "Roles", "/admin/roles"),
        new("Tenants", "tenants", "Tenants", "/admin/tenants"),
        new("TenantSettings", "tenants", "Tenants", "/admin/tenants/{id}/settings"),
        new("TenantMembers", "tenants", "Tenants", "/admin/tenants/{id}/members"),
        new("Settings", "settings", "Settings", "/admin/settings"),
        new("Audit", "audit", "Audit", "/audit"),
        new("Health", "health", "Health", "/system/health"),
        new("Profile", "profile", "Profile", "/account/profile"),
        new("Notifications", "notifications", "Notifications", "/notifications"),
        new("NotificationPreferences", "notification-preferences", "NotificationPreferences", "/account/notification-preferences"),
    ];

    /// <summary>The page keys a <c>--pages</c> value can name (<c>tenants</c> covers the list, settings and members pages).</summary>
    public static IReadOnlyList<string> Keys { get; } = Pages.Select(p => p.Key).Distinct().ToList();

    /// <summary>Parses <c>--pages users,roles</c>; null or empty means every page. Unknown names fail with the list of valid ones.</summary>
    public static IReadOnlyList<string> ParseKeys(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            return Keys;
        var asked = value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.ToLowerInvariant()).Distinct().ToList();
        var unknown = asked.Where(k => !Keys.Contains(k)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown admin page '{unknown[0]}'. Choose from: {string.Join(", ", Keys)}.");
        return asked;
    }

    /// <summary>One manifest entry: where the file goes in the UI project and which template renders it.</summary>
    internal sealed record Entry(string Path, string Template);

    /// <summary>The <c>admin</c> feature's files for an engine, read from <c>ui-kit.json</c> next to the unpacked templates folder.</summary>
    public static IReadOnlyList<Entry> ReadEntries(string templatesDir, string engine)
    {
        var manifest = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(templatesDir.TrimEnd(System.IO.Path.DirectorySeparatorChar))!, UiTemplatePackage.ManifestFile);
        if (!File.Exists(manifest))
            throw new InvalidOperationException($"{UiTemplatePackage.ManifestFile} is missing from the {UiTemplatePackage.PackageId} package.");

        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        if (!doc.RootElement.TryGetProperty("features", out var features)
            || !features.TryGetProperty(Feature, out var admin)
            || !admin.TryGetProperty("files", out var files)
            || !files.TryGetProperty(engine, out var list))
            throw new InvalidOperationException(
                $"This {UiTemplatePackage.PackageId} version has no '{Feature}' feature for {engine}. Update the package (MODULUS_UI_VERSION) and try again.");

        return list.EnumerateArray()
            .Select(e => new Entry(e.GetProperty("path").GetString()!, e.GetProperty("template").GetString()!))
            .ToList();
    }

    /// <summary>The page a manifest path belongs to, or null for the MVC controller.</summary>
    public static UiAdminPage? PageOf(string path)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(path);
        return Pages.FirstOrDefault(p => p.File.Equals(stem, StringComparison.Ordinal));
    }

    /// <summary>The entries that belong to the chosen pages (the MVC controller is always kept: it serves every action).</summary>
    public static IReadOnlyList<Entry> Select(IReadOnlyList<Entry> entries, IReadOnlyList<string> keys) =>
        entries.Where(e => PageOf(e.Path) is not { } page || keys.Contains(page.Key)).ToList();

    /// <summary>The <c>AddModulusAdminUi</c> statement for the chosen pages; the pages argument is left out when every page is chosen.</summary>
    public static string WiringLine(IReadOnlyList<string> keys)
    {
        if (keys.Count == Keys.Count)
            return "builder.Services.AddModulusAdminUi();";
        var flags = Pages.Where(p => keys.Contains(p.Key)).Select(p => p.Flag).Distinct()
            .Select(f => "Modulus.Ui.Abstractions.Admin.AdminPages." + f);
        return $"builder.Services.AddModulusAdminUi(o => o.Pages = {string.Join(" | ", flags)});";
    }
}
