namespace Modulus.UI.Identity.Pages.Account;

using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Identity.Abstractions;
using Modulus.Localization;

/// <summary>
/// Self-registration form (<c>/Account/Register</c>). Gated by
/// <see cref="IdentityUiOptions.AllowSelfRegistration"/> — 404 when off.
/// New users inherit the ambient tenant so a tenant-scoped registration page
/// creates tenant members, not host users.
/// </summary>
[AllowAnonymous]
public sealed class RegisterModel(
    UserManager<ModulusUser> userManager,
    SignInManager<ModulusUser> signInManager,
    ICurrentTenant currentTenant,
    IOptions<IdentityUiOptions> uiOptions,
    IOptions<ModulusIdentityOptions> identityOptions,
    IModulusLocalizer localizer) : PageModel
{
    private readonly UserManager<ModulusUser> _userManager = userManager;
    private readonly SignInManager<ModulusUser> _signInManager = signInManager;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly IdentityUiOptions _uiOptions = uiOptions.Value;
    private readonly ModulusIdentityOptions _identityOptions = identityOptions.Value;
    private readonly IModulusLocalizer _localizer = localizer;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (!_uiOptions.AllowSelfRegistration)
            return NotFound();

        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!_uiOptions.AllowSelfRegistration)
            return NotFound();

        ReturnUrl = returnUrl;
        if (!ModelState.IsValid)
            return Page();

        if (!string.Equals(Input.Password, Input.ConfirmPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError(
                $"{nameof(Input)}.{nameof(InputModel.ConfirmPassword)}",
                await TextAsync("Register.PasswordMismatch"));
            return Page();
        }

        var user = new ModulusUser
        {
            UserName = Input.Email,
            Email = Input.Email,
            TenantId = _currentTenant.TenantId,
        };

        var result = await _userManager.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        if (_identityOptions.RequireConfirmedEmail)
        {
            TempData["StatusMessage"] = await TextAsync("Register.ConfirmEmailNotice");
            return RedirectToPage("./Login", new { returnUrl });
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : Redirect("~/");
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(IdentityUiLocalization.ResourceName, key);

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
