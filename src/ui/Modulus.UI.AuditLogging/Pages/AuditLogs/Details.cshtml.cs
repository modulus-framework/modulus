namespace Modulus.UI.AuditLogging.Pages.AuditLogs;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.AuditLogging;
using Modulus.Core.Abstractions;
using Modulus.Localization;

/// <summary>
/// Audit-entry details (<c>/audit-logs/{id}</c>): full field dump; 404 for
/// unknown ids and for ids outside the ambient tenant (masked as unknown,
/// not a separate "forbidden" response, so a caller can't use this page to
/// probe which ids exist in other tenants).
/// </summary>
[Authorize]
public sealed class DetailsModel(
    IAuditLogStore store,
    ICurrentTenant currentTenant,
    IModulusLocalizer localizer) : PageModel
{
    private readonly IAuditLogStore _store = store;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly IModulusLocalizer _localizer = localizer;

    public AuditLogEntry? Entry { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Entry = await _store.GetOrNullAsync(id, ct);
        if (Entry is null || !VisibleToCaller(Entry))
        {
            Entry = null;
            return NotFound();
        }

        return Page();
    }

    /// <summary>
    /// GetOrNullAsync has no tenant parameter (IAuditLogStore expects the
    /// caller to apply tenant scoping, same as QueryAsync's TenantId filter —
    /// see IndexModel), so this checks the fetched entry directly: the host
    /// sees any entry; a resolved tenant only its own; an unresolved tenant
    /// (no header, a misconfigured resolver) nothing at all, entry.TenantId
    /// included — the same fail-closed shape as the index page.
    /// </summary>
    private bool VisibleToCaller(AuditLogEntry entry)
        => _currentTenant.IsHost
            || (_currentTenant.TenantId is { } tenantId && entry.TenantId == tenantId);

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(AuditLoggingUiLocalization.ResourceName, key);
}
