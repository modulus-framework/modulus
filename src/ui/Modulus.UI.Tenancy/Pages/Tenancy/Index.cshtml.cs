namespace Modulus.UI.Tenancy.Pages.Tenancy;

using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.Core.Abstractions;
using Modulus.Localization;
using Modulus.MultiTenancy;

/// <summary>
/// Tenant directory (<c>/tenancy</c>): the ambient tenant plus every tenant
/// the store can list. The default store lists nothing (listing is an
/// opt-in fan-out surface) — the page then shows the empty-state hint
/// instead of an empty table.
/// </summary>
public sealed class IndexModel(
    ICurrentTenant currentTenant,
    ITenantStore tenantStore,
    IModulusLocalizer localizer) : PageModel
{
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly ITenantStore _tenantStore = tenantStore;
    private readonly IModulusLocalizer _localizer = localizer;

    public string CurrentDescription { get; private set; } = string.Empty;

    public IReadOnlyList<TenantInfo> Tenants { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Tenants = await _tenantStore.ListAsync(ct);
        CurrentDescription = _currentTenant.IsHost || _currentTenant.TenantSlug is null
            ? await TextAsync("Directory.Host")
            : $"{_currentTenant.TenantSlug} ({_currentTenant.TenantId})";
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(TenancyUiLocalization.ResourceName, key);
}
