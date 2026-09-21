namespace Modulus.UI.Tenancy.Pages.Tenancy;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.Core.Abstractions;
using Modulus.Localization;
using Modulus.MultiTenancy;

/// <summary>Tenant details (<c>/tenancy/{slug}</c>): unknown slugs are 404.</summary>
[Authorize]
public sealed class DetailsModel(
    ITenantStore tenantStore,
    IModulusLocalizer localizer) : PageModel
{
    private readonly ITenantStore _tenantStore = tenantStore;
    private readonly IModulusLocalizer _localizer = localizer;

    public TenantInfo? Tenant { get; private set; }

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken ct)
    {
        Tenant = await _tenantStore.FindBySlugAsync(slug, ct);
        if (Tenant is null)
        {
            ModelState.AddModelError(string.Empty, await TextAsync("NotFound"));
            return NotFound();
        }

        return Page();
    }

    private Task<string> TextAsync(string key)
        => _localizer.GetAsync(TenancyUiLocalization.ResourceName, $"Details.{key}");
}
