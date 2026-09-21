namespace Modulus.UI.Notifications;

/// <summary>Permission names declared by the Notifications UI.</summary>
public static class NotificationsUiPermissions
{
    /// <summary>View and manage your own notifications.</summary>
    public const string View = "notifications:view";
}

/// <summary>Options for the Notifications UI, bound from the <c>NotificationsUi</c> section.</summary>
public sealed class NotificationsUiOptions
{
    public const string SectionName = "NotificationsUi";

    /// <summary>
    /// Razor Pages authorization policy applied to the <c>/Notifications</c>
    /// folder. Defaults to <c>null</c> (pages open) so the UI works without
    /// the authorization stack; set to
    /// <see cref="NotificationsUiPermissions.View"/> once
    /// <c>AddModulusAuthorization</c> wires the <c>:</c>-policy convention.
    /// Declared on the registry by <c>AddModulusNotificationsUi</c>. The
    /// inbox itself is per-user (ambient <c>ICurrentUser</c>), so anonymous
    /// callers see a sign-in prompt rather than data.
    /// </summary>
    public string? RequirePermission { get; set; }

    /// <summary>Default page size for the inbox. Defaults to 20.</summary>
    public int DefaultPageSize { get; set; } = 20;
}
