namespace Modulus.UI.Identity.Pages.Account;

using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.Localization;

/// <summary>Authenticated-but-forbidden page (<c>/Account/AccessDenied</c>).</summary>
public sealed class AccessDeniedModel(IModulusLocalizer localizer) : PageModel
{
    private readonly IModulusLocalizer _localizer = localizer;

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(IdentityUiLocalization.ResourceName, key);
}
