namespace Modulus.UI.Identity.Pages.Account;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.Identity.Abstractions;
using Modulus.Localization;

/// <summary>
/// HTML sign-out (<c>/Account/SignOut</c>). Lives apart from the JSON
/// <c>POST /account/logout</c> account endpoint on purpose: sharing that
/// route + verb would make endpoint routing throw
/// <c>AmbiguousMatchException</c>. GET renders a confirmation; POST signs out
/// (cookie + external schemes) and lands on <c>/Account/LoggedOut</c>.
/// </summary>
public sealed class SignOutModel(
    SignInManager<ModulusUser> signInManager,
    IModulusLocalizer localizer) : PageModel
{
    private readonly SignInManager<ModulusUser> _signInManager = signInManager;
    private readonly IModulusLocalizer _localizer = localizer;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await _signInManager.SignOutAsync();
        return RedirectToPage("./LoggedOut");
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(IdentityUiLocalization.ResourceName, key);
}
