namespace Modulus.UI.AuditLogging.Pages.AuditLogs;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Modulus.AuditLogging;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Common;
using Modulus.Localization;

/// <summary>
/// Audit-log browser (<c>/audit-logs</c>): filterable, paged list of entries
/// (action / resource / user / time window), scoped to the ambient tenant.
/// Unknown-user-id input renders a validation error instead of querying.
/// </summary>
[Authorize]
public sealed class IndexModel(
    IAuditLogStore store,
    ICurrentTenant currentTenant,
    IOptions<AuditLoggingUiOptions> options,
    IModulusLocalizer localizer) : PageModel
{
    private readonly IAuditLogStore _store = store;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly IModulusLocalizer _localizer = localizer;
    private readonly IOptions<AuditLoggingUiOptions> _options = options;

    [BindProperty(SupportsGet = true)]
    public string? Action { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Resource { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTimeOffset? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public PagedList<AuditLogEntry> Result { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        // AuditLogQuery.TenantId is a host escape hatch — null means "no
        // tenant filter, every tenant's rows" (IAuditLogStore's own contract:
        // "callers pass the tenant filter explicitly so host operators can
        // query across tenants"). A non-host caller with no tenant resolved
        // (a missing header, a misconfigured resolver) must never reach the
        // store with that null, or it would see every tenant's audit trail
        // instead of none — so that case skips the query entirely, same
        // fail-closed shape as the Notifications inbox for an anonymous user.
        if (!_currentTenant.IsHost && _currentTenant.TenantId is null)
            return;

        Guid? userId = null;
        if (!string.IsNullOrWhiteSpace(UserId))
        {
            if (!Guid.TryParse(UserId, out var parsed))
            {
                ModelState.AddModelError(
                    nameof(UserId), await TextAsync("Index.InvalidUserId"));
                return;
            }

            userId = parsed;
        }

        var pageSize = _options.Value.DefaultPageSize;
        Result = await _store.QueryAsync(new AuditLogQuery
        {
            TenantId = _currentTenant.IsHost ? null : _currentTenant.TenantId,
            Action = string.IsNullOrWhiteSpace(Action) ? null : Action,
            Resource = string.IsNullOrWhiteSpace(Resource) ? null : Resource,
            UserId = userId,
            From = From,
            To = To,
            Page = PageNumber < 1 ? 1 : PageNumber,
            PageSize = pageSize,
        }, ct);
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(AuditLoggingUiLocalization.ResourceName, key);
}
