namespace Modulus.UI.Permissions.Pages.Permissions;

using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.Core.Abstractions;
using Modulus.Localization;

/// <summary>
/// Permission catalog (<c>/permissions</c>): every registry entry grouped by
/// module prefix (the segment before <c>:</c>, mirroring
/// <c>GetByModule</c>). Ungrouped names fall under <c>General</c>.
/// </summary>
public sealed class IndexModel(
    IPermissionRegistry registry,
    IModulusLocalizer localizer) : PageModel
{
    private readonly IPermissionRegistry _registry = registry;
    private readonly IModulusLocalizer _localizer = localizer;

    public IReadOnlyList<PermissionGroup> Groups { get; private set; } = [];

    public void OnGet()
    {
        Groups = [.. _registry.GetAll()
            .GroupBy(p => ModulePrefix(p.Permission))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PermissionGroup(
                g.Key,
                [.. g.OrderBy(p => p.Permission, StringComparer.OrdinalIgnoreCase)]))];
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(PermissionsUiLocalization.ResourceName, key);

    private static string ModulePrefix(string permission)
    {
        var separator = permission.IndexOf(':');
        return separator > 0 ? permission[..separator] : "General";
    }

    public sealed record PermissionGroup(
        string Module,
        IReadOnlyList<PermissionDefinition> Permissions);
}
