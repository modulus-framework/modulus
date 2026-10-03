using Microsoft.AspNetCore.Authorization;
namespace Modulus.UI.Identity.Pages.Account;

using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.Localization;
using Modulus.Core.Abstractions.Security;

/// <summary>Post-sign-out confirmation (<c>/Account/LoggedOut</c>).</summary>
[AllowAnonymous]
[Loosened("Shown after sign-out, when there is no session any more", Framework = true)]
public sealed class LoggedOutModel(IModulusLocalizer localizer) : PageModel
{
    private readonly IModulusLocalizer _localizer = localizer;

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(IdentityUiLocalization.ResourceName, key);
}
