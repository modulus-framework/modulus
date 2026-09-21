namespace Modulus.UI.Users.Pages.Users;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Modulus.Identity.Abstractions;
using Modulus.Localization;
using Modulus.UI;

/// <summary>
/// User details (<c>/users/{id}</c>): profile fields, assigned roles, and
/// lockout state, with activate/deactivate, lock/unlock, and role
/// assignment handlers. Unknown ids are 404; failed Identity operations
/// surface as model errors on the same page.
/// </summary>
/// <remarks>
/// HTMX behavior: status mutations re-render the account card
/// (<c>_UserCard</c>), role mutations re-render the roles region
/// (<c>_RoleList</c>), each with a toast on success and inline errors on
/// failure. Non-JS callers keep the classic redirect/page flow.
/// </remarks>
public sealed class DetailsModel(
    UserManager<ModulusUser> users,
    RoleManager<ModulusRole> roles,
    IModulusLocalizer localizer) : HtmxPageModel
{
    private readonly UserManager<ModulusUser> _users = users;
    private readonly RoleManager<ModulusRole> _roles = roles;
    private readonly IModulusLocalizer _localizer = localizer;

    public ModulusUser? Account { get; private set; }

    public IReadOnlyList<string> AssignedRoles { get; private set; } = [];

    public bool LockedOut { get; private set; }

    public IReadOnlyList<string> AvailableRoles { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (!await LoadAsync(id))
            return NotFound();

        return Page();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(Guid id)
    {
        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        user.IsActive = !user.IsActive;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            AddErrors(result);
            await LoadAsync(id);
            return IsHtmxRequest ? HtmxPartial("_UserCard", this) : Page();
        }

        if (!IsHtmxRequest)
            return RedirectToPage(new { id });

        await LoadAsync(id);
        HtmxToast(await TextAsync("Details.Updated"));
        return HtmxPartial("_UserCard", this);
    }

    public async Task<IActionResult> OnPostLockAsync(Guid id)
    {
        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        await _users.SetLockoutEnabledAsync(user, true);
        await _users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        if (!IsHtmxRequest)
            return RedirectToPage(new { id });

        await LoadAsync(id);
        HtmxToast(await TextAsync("Details.Updated"));
        return HtmxPartial("_UserCard", this);
    }

    public async Task<IActionResult> OnPostUnlockAsync(Guid id)
    {
        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        await _users.SetLockoutEndDateAsync(user, null);
        await _users.ResetAccessFailedCountAsync(user);

        if (!IsHtmxRequest)
            return RedirectToPage(new { id });

        await LoadAsync(id);
        HtmxToast(await TextAsync("Details.Updated"));
        return HtmxPartial("_UserCard", this);
    }

    public async Task<IActionResult> OnPostAddRoleAsync(Guid id, string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            ModelState.AddModelError(
                nameof(roleName), await TextAsync("Details.RoleRequired"));
            await LoadAsync(id);
            return IsHtmxRequest ? HtmxPartial("_RoleList", this) : Page();
        }

        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        var result = await _users.AddToRoleAsync(user, roleName);
        if (!result.Succeeded)
        {
            AddErrors(result);
            await LoadAsync(id);
            return IsHtmxRequest ? HtmxPartial("_RoleList", this) : Page();
        }

        if (!IsHtmxRequest)
            return RedirectToPage(new { id });

        await LoadAsync(id);
        HtmxToast(await TextAsync("Details.RoleAdded"));
        return HtmxPartial("_RoleList", this);
    }

    public async Task<IActionResult> OnPostRemoveRoleAsync(Guid id, string roleName)
    {
        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null)
            return NotFound();

        var result = await _users.RemoveFromRoleAsync(user, roleName);
        if (!result.Succeeded)
        {
            AddErrors(result);
            await LoadAsync(id);
            return IsHtmxRequest ? HtmxPartial("_RoleList", this) : Page();
        }

        if (!IsHtmxRequest)
            return RedirectToPage(new { id });

        await LoadAsync(id);
        HtmxToast(await TextAsync("Details.RoleRemoved"));
        return HtmxPartial("_RoleList", this);
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(UsersUiLocalization.ResourceName, key);

    private async Task<bool> LoadAsync(Guid id)
    {
        var user = await _users.FindByIdAsync(id.ToString());
        if (user is null)
            return false;

        Account = user;
        AssignedRoles = [.. await _users.GetRolesAsync(user)];
        LockedOut = await _users.IsLockedOutAsync(user);
        AvailableRoles = _roles.Roles
            .Select(r => r.Name)
            .Where(n => n != null)
            .Cast<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return true;
    }

    private void AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
    }
}
