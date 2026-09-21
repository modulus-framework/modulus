namespace Modulus.UI.Users.Pages.Roles;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Modulus.Identity.Abstractions;
using Modulus.Localization;
using Modulus.UI;
using Modulus.UI.Users;

/// <summary>
/// Role catalog (<c>/roles</c>): first <c>UsersUi:ListLimit</c> roles ordered
/// by name, with inline create and delete. Membership is managed from the
/// user details page.
/// </summary>
/// <remarks>
/// HTMX behavior: deletes re-render the role table (<c>_RoleTable</c>) in
/// place with a toast; failures re-render with inline errors. Creates stay
/// full-page so field validation renders next to the create form. Non-JS
/// callers keep the classic redirect flow.
/// </remarks>
[Authorize]
public sealed class IndexModel(
    RoleManager<ModulusRole> roles,
    IOptions<UsersUiOptions> options,
    IModulusLocalizer localizer) : HtmxPageModel
{
    private readonly RoleManager<ModulusRole> _roles = roles;
    private readonly IOptions<UsersUiOptions> _options = options;
    private readonly IModulusLocalizer _localizer = localizer;

    public IReadOnlyList<ModulusRole> Rows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Rows = _roles.Roles
            .OrderBy(r => r.Name)
            .Take(_options.Value.ListLimit)
            .ToList();
        await Task.CompletedTask;
    }

    public async Task<IActionResult> OnPostCreateAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(
                nameof(name), await TextAsync("Roles.NameRequired"));
            await OnGetAsync(default);
            return Page();
        }

        var result = await _roles.CreateAsync(new ModulusRole { Name = name });
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            await OnGetAsync(default);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var role = await _roles.FindByIdAsync(id.ToString());
        if (role is null)
            return NotFound();

        var result = await _roles.DeleteAsync(role);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            await OnGetAsync(default);
            return IsHtmxRequest ? HtmxPartial("_RoleTable", this) : Page();
        }

        if (!IsHtmxRequest)
            return RedirectToPage();

        await OnGetAsync(default);
        HtmxToast(await TextAsync("Roles.Deleted"));
        return HtmxPartial("_RoleTable", this);
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(UsersUiLocalization.ResourceName, key);
}
