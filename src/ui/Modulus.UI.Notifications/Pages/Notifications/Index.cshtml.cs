namespace Modulus.UI.Notifications.Pages.Notifications;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Common;
using Modulus.Localization;
using Modulus.MultiTenancy;
using Modulus.Notifications;
using Modulus.UI;

/// <summary>
/// Per-user notification inbox (<c>/notifications</c>): ambient user's rows
/// scoped to the ambient tenant, with mark-read / mark-all-read / delete.
/// Anonymous callers see a sign-in prompt instead of data; every mutating
/// handler re-checks the ambient user id so one user cannot touch another's
/// rows by guessing ids. Publishing stays in app code via
/// <see cref="INotificationPublisher"/> (no UI publish form by design).
/// </summary>
/// <remarks>
/// HTMX behavior: row mutations swap in place (read rows vanish from the
/// unread-only view, otherwise the row re-renders with its new state; deletes
/// always remove the row; mark-all re-renders the list) with a toast
/// confirmation. Non-JS callers keep the classic redirect flow. Filter and
/// pager stay full-page GETs so URLs remain bookmarkable.
/// </remarks>
public sealed class IndexModel(
    INotificationStore store,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IOptions<NotificationsUiOptions> options,
    IModulusLocalizer localizer) : HtmxPageModel
{
    private readonly INotificationStore _store = store;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ICurrentTenant _currentTenant = currentTenant;
    private readonly IOptions<NotificationsUiOptions> _options = options;
    private readonly IModulusLocalizer _localizer = localizer;

    [BindProperty(SupportsGet = true)]
    public bool UnreadOnly { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public PagedList<UserNotification> Result { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (_currentUser.UserId is null)
            return;

        Result = await QueryAsync(ct);
    }

    public async Task<IActionResult> OnPostMarkReadAsync(Guid id, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return IsHtmxRequest ? HtmxEmpty() : RedirectToPage(new { UnreadOnly, PageNumber });

        await _store.MarkAsReadAsync(id, userId, ct);

        if (!IsHtmxRequest)
            return RedirectToPage(new { UnreadOnly, PageNumber });

        if (UnreadOnly)
        {
            HtmxToast(await TextAsync("Index.MarkedRead"));
            return HtmxEmpty();
        }

        var row = await FindRowAsync(id, ct);
        if (row is null)
            return HtmxEmpty();

        HtmxToast(await TextAsync("Index.MarkedRead"));
        return HtmxPartial("_NotificationRow", row);
    }

    public async Task<IActionResult> OnPostMarkAllReadAsync(CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return IsHtmxRequest ? HtmxEmpty() : RedirectToPage(new { UnreadOnly });

        await _store.MarkAllAsReadAsync(userId, _currentTenant.TenantId, ct);

        if (!IsHtmxRequest)
            return RedirectToPage(new { UnreadOnly });

        Result = await QueryAsync(ct);
        HtmxToast(await TextAsync("Index.AllMarkedRead"));
        return HtmxPartial("_NotificationList", ListView());
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return IsHtmxRequest ? HtmxEmpty() : RedirectToPage(new { UnreadOnly, PageNumber });

        await _store.DeleteAsync(id, userId, ct);

        if (!IsHtmxRequest)
            return RedirectToPage(new { UnreadOnly, PageNumber });

        HtmxToast(await TextAsync("Index.Deleted"));
        return HtmxEmpty();
    }

    public Task<string> TextAsync(string key)
        => _localizer.GetAsync(NotificationsUiLocalization.ResourceName, key);

    /// <summary>Builds the list fragment model from the current result.</summary>
    public NotificationListView ListView() => new(Result, UnreadOnly);

    /// <summary>Builds the row fragment model for one notification.</summary>
    public NotificationRowView RowView(UserNotification item)
        => new(item, UnreadOnly, PageNumber);

    private Task<PagedList<UserNotification>> QueryAsync(CancellationToken ct)
        => _store.ListAsync(
            _currentUser.UserId!.Value,
            _currentTenant.TenantId,
            UnreadOnly,
            PageNumber < 1 ? 1 : PageNumber,
            _options.Value.DefaultPageSize,
            ct);

    private async Task<NotificationRowView?> FindRowAsync(Guid id, CancellationToken ct)
    {
        var list = await QueryAsync(ct);
        var item = list.Items.FirstOrDefault(i => i.Id == id);
        return item is null ? null : RowView(item);
    }
}

/// <summary>
/// Fragment model for one inbox row (<c>_NotificationRow</c> partial).
/// </summary>
/// <param name="Item">The notification to render.</param>
/// <param name="UnreadOnly">Current filter (round-tripped on row actions).</param>
/// <param name="PageNumber">Current page (round-tripped on row actions).</param>
public sealed record NotificationRowView(UserNotification Item, bool UnreadOnly, int PageNumber);

/// <summary>
/// Fragment model for the inbox list (<c>_NotificationList</c> partial).
/// </summary>
/// <param name="Result">Current page of notifications.</param>
/// <param name="UnreadOnly">Current filter (round-tripped on row actions).</param>
public sealed record NotificationListView(PagedList<UserNotification> Result, bool UnreadOnly);
