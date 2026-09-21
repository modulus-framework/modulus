namespace Modulus.UI.Identity.Pages.Account;

using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.Identity.Abstractions;
using Modulus.Localization;

/// <summary>
/// Cookie sign-in form (<c>/Account/Login</c> — matches the cookie
/// middleware's <c>LoginPath</c>). Mirrors the password-grant validator's
/// semantics: inactive users are rejected, lock-out is honoured, sign-in
/// requirements (confirmed email) are enforced. Failures are non-committal
/// (unknown email, inactive account, and bad password all render the same
/// message) so the form cannot enumerate accounts.
/// <para>
/// Anonymous credential endpoint: keep <c>UseModulusRateLimiting</c> wired so
/// the per-IP bucket bounds password-guessing.
/// </para>
/// </summary>
[AllowAnonymous]
public sealed class LoginModel(
    SignInManager<ModulusUser> signInManager,
    UserManager<ModulusUser> userManager,
    IModulusLocalizer localizer) : PageModel
{
    private readonly SignInManager<ModulusUser> _signInManager = signInManager;
    private readonly UserManager<ModulusUser> _userManager = userManager;
    private readonly IModulusLocalizer _localizer = localizer;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        if (!ModelState.IsValid)
            return Page();

        // Registration sets UserName = Email, so a single email lookup
        // covers both. One lookup (not name-then-email) keeps timing uniform.
        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user is null || !user.IsActive)
        {
            // Unknown email, inactive account, and (below) bad password all
            // render the same message: the form cannot enumerate accounts.
            ModelState.AddModelError(string.Empty, await TextAsync("Login.InvalidAttempt"));
            return Page();
        }

        if (!await _signInManager.CanSignInAsync(user))
        {
            // Unconfirmed email gets a distinct hint — the user needs to know
            // what to do next.
            ModelState.AddModelError(string.Empty, await TextAsync("Login.EmailNotConfirmed"));
            return Page();
        }

        var check = await _signInManager.PasswordSignInAsync(
            user, Input.Password, Input.RememberMe, lockoutOnFailure: true);

        if (check.Succeeded)
            return LocalRedirectOrHome(returnUrl);

        if (check.IsLockedOut)
            ModelState.AddModelError(string.Empty, await TextAsync("Login.LockedOut"));
        else if (check.RequiresTwoFactor)
            ModelState.AddModelError(string.Empty, await TextAsync("Login.RequiresTwoFactor"));
        else
            ModelState.AddModelError(string.Empty, await TextAsync("Login.InvalidAttempt"));

        return Page();
    }

    private IActionResult LocalRedirectOrHome(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : Redirect("~/");

    private Task<string> TextAsync(string key)
        => _localizer.GetAsync(IdentityUiLocalization.ResourceName, key);

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}
