namespace Modulus.UI.Settings.Pages.Settings;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.Localization;
using Modulus.Settings;

/// <summary>
/// Setting browser (<c>/settings</c>): visible definitions grouped by the
/// first dotted segment, each with its effective value for the ambient
/// scope (user → tenant → global → default).
/// </summary>
[Authorize]
public sealed class IndexModel(
    ISettingDefinitionRegistry registry,
    ISettingManager manager,
    IModulusLocalizer localizer) : PageModel
{
    private readonly ISettingDefinitionRegistry _registry = registry;
    private readonly ISettingManager _manager = manager;
    private readonly IModulusLocalizer _localizer = localizer;

    public IReadOnlyList<SettingGroup> Groups { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        var rows = new List<SettingRow>();
        foreach (var definition in _registry.List().Where(d => d.IsVisibleToClients))
            rows.Add(new SettingRow(definition, await _manager.GetOrNullAsync(definition.Name, ct)));

        Groups = [.. rows
            .GroupBy(r => GroupPrefix(r.Definition.Name))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SettingGroup(
                g.Key,
                [.. g.OrderBy(r => r.Definition.Name, StringComparer.OrdinalIgnoreCase)]))];
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(SettingsUiLocalization.ResourceName, key);

    private static string GroupPrefix(string name)
    {
        var dot = name.IndexOf('.');
        return dot > 0 ? name[..dot] : "General";
    }

    public sealed record SettingRow(SettingDefinition Definition, string? EffectiveValue);

    public sealed record SettingGroup(string Group, IReadOnlyList<SettingRow> Rows);
}
