namespace Modulus.UI.Identity.Pages.Account;

using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.Localization;

/// <summary>Post-sign-out confirmation (<c>/Account/LoggedOut</c>).</summary>
public sealed class LoggedOutModel(IModulusLocalizer localizer) : PageModel
{
    private readonly IModulusLocalizer _localizer = localizer;

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(IdentityUiLocalization.ResourceName, key);
}
