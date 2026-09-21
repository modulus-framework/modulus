namespace Modulus.UI.AuditLogging.Pages.AuditLogs;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Modulus.AuditLogging;
using Modulus.Localization;

/// <summary>
/// Audit-entry details (<c>/audit-logs/{id}</c>): full field dump; 404 for
/// unknown ids.
/// </summary>
[Authorize]
public sealed class DetailsModel(
    IAuditLogStore store,
    IModulusLocalizer localizer) : PageModel
{
    private readonly IAuditLogStore _store = store;
    private readonly IModulusLocalizer _localizer = localizer;

    public AuditLogEntry? Entry { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Entry = await _store.GetOrNullAsync(id, ct);
        if (Entry is null)
            return NotFound();

        return Page();
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(AuditLoggingUiLocalization.ResourceName, key);
}
