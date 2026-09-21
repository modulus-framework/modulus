namespace Modulus.UI.Settings.Pages.Settings;

using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modulus.Core.Abstractions;
using Modulus.Localization;
using Modulus.MultiTenancy;
using Modulus.Settings;
using Modulus.UI;

/// <summary>
/// Setting editor (<c>/settings/edit/{name}</c>): writes target one explicit
/// scope (the manager resolves ambient tenant/user for tenant/user scopes).
/// An empty value removes the scoped override instead of storing blank.
/// Unknown names are 404.
/// </summary>
/// <remarks>
/// HTMX behavior: saves re-render the form (<c>_EditForm</c>) in place with
/// a toast; validation failures re-render with inline errors. Non-JS callers
/// keep the classic redirect/page flow.
/// </remarks>
[Authorize]
public sealed class EditModel(
    ISettingDefinitionRegistry registry,
    ISettingManager manager,
    ICurrentTenant currentTenant,
    ICurrentUser currentUser,
    IModulusLocalizer localizer) : HtmxPageModel
{
    private readonly ISettingDefinitionRegistry _registry = registry;
    private readonly ISettingManager _manager = manager;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IModulusLocalizer _localizer = localizer;

    public SettingDefinition? Definition { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string name, CancellationToken ct)
    {
        Definition = _registry.Find(name);
        if (Definition is null)
            return NotFound();

        Input = new InputModel
        {
            Value = await _manager.GetOrNullAsync(name, ct),
            Scope = nameof(SettingScope.Global),
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string name, CancellationToken ct)
    {
        Definition = _registry.Find(name);
        if (Definition is null)
            return NotFound();

        if (!ModelState.IsValid)
            return HtmxOrPage();

        if (!Enum.TryParse<SettingScope>(Input.Scope, ignoreCase: true, out var scope))
        {
            ModelState.AddModelError(nameof(Input.Scope), await TextAsync("Edit.UnknownScope"));
            return HtmxOrPage();
        }

        if (scope is SettingScope.Tenant && _currentTenant.TenantId is null)
        {
            ModelState.AddModelError(nameof(Input.Scope), await TextAsync("Edit.TenantRequired"));
            return HtmxOrPage();
        }

        if (scope is SettingScope.User && _currentUser.UserId is null)
        {
            ModelState.AddModelError(nameof(Input.Scope), await TextAsync("Edit.UserRequired"));
            return HtmxOrPage();
        }

        if (string.IsNullOrWhiteSpace(Input.Value))
            await _manager.RemoveAsync(name, scope, ct);
        else
            await _manager.SetAsync(name, Input.Value, scope, ct);

        if (!IsHtmxRequest)
            return RedirectToPage("./Index");

        Input = new InputModel
        {
            Value = await _manager.GetOrNullAsync(name, ct),
            Scope = Input.Scope,
        };
        HtmxToast(await TextAsync("Edit.Saved"));
        return HtmxPartial("_EditForm", this);
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(SettingsUiLocalization.ResourceName, key);

    private IActionResult HtmxOrPage()
        => IsHtmxRequest ? HtmxPartial("_EditForm", this) : Page();

    public sealed class InputModel
    {
        public string? Value { get; set; }

        [Required]
        public string Scope { get; set; } = nameof(SettingScope.Global);
    }
}
