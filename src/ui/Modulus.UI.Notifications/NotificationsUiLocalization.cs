namespace Modulus.UI.Notifications;

using Modulus.Localization;

/// <summary>
/// Localizer strings for the Notifications UI (<c>Modulus.Notifications</c>
/// resource). Seeded at startup when an <see cref="ILocalizationStore"/> is
/// registered; hosts override individual keys by re-seeding after this runs.
/// </summary>
public static class NotificationsUiLocalization
{
    public const string ResourceName = "Modulus.Notifications";

    public static async Task SeedAsync(ILocalizationStore store, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        foreach (var (key, value) in English)
            await store.SetAsync(ResourceName, "en", key, value, ct).ConfigureAwait(false);
        foreach (var (key, value) in Spanish)
            await store.SetAsync(ResourceName, "es", key, value, ct).ConfigureAwait(false);
    }

    private static readonly IReadOnlyDictionary<string, string> English =
        new Dictionary<string, string>
        {
            ["Index.Title"] = "Notifications",
            ["Index.UnreadOnly"] = "Unread only",
            ["Index.Filter"] = "Filter",
            ["Index.MarkRead"] = "Mark read",
            ["Index.MarkAllRead"] = "Mark all as read",
            ["Index.Delete"] = "Delete",
            ["Index.ConfirmDelete"] = "Delete this notification?",
            ["Index.MarkedRead"] = "Marked as read.",
            ["Index.AllMarkedRead"] = "All notifications marked as read.",
            ["Index.Deleted"] = "Notification deleted.",
            ["Index.Empty"] = "No notifications.",
            ["Index.SignIn"] = "Sign in to see your notifications.",
            ["Index.Anonymous"] = "Anonymous",
        };

    private static readonly IReadOnlyDictionary<string, string> Spanish =
        new Dictionary<string, string>
        {
            ["Index.Title"] = "Notificaciones",
            ["Index.UnreadOnly"] = "Solo no leídas",
            ["Index.Filter"] = "Filtrar",
            ["Index.MarkRead"] = "Marcar leída",
            ["Index.MarkAllRead"] = "Marcar todas como leídas",
            ["Index.Delete"] = "Eliminar",
            ["Index.ConfirmDelete"] = "¿Eliminar esta notificación?",
            ["Index.MarkedRead"] = "Marcada como leída.",
            ["Index.AllMarkedRead"] = "Todas las notificaciones marcadas como leídas.",
            ["Index.Deleted"] = "Notificación eliminada.",
            ["Index.Empty"] = "Sin notificaciones.",
            ["Index.SignIn"] = "Inicia sesión para ver tus notificaciones.",
            ["Index.Anonymous"] = "Anónimo",
        };
}
