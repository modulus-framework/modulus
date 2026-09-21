namespace Modulus.UI.AuditLogging;

/// <summary>Permission names declared by the Audit Logging UI.</summary>
public static class AuditLoggingUiPermissions
{
    /// <summary>View audit-log entries.</summary>
    public const string View = "audit:view";
}

/// <summary>Options for the Audit Logging UI, bound from the <c>AuditLoggingUi</c> section.</summary>
public sealed class AuditLoggingUiOptions
{
    public const string SectionName = "AuditLoggingUi";

    /// <summary>
    /// Razor Pages authorization policy applied to the <c>/AuditLogs</c>
    /// folder. Defaults to <see cref="AuditLoggingUiPermissions.View"/> —
    /// every page model also carries a bare <c>[Authorize]</c> as an
    /// unconditional floor, so anonymous access is impossible even before
    /// <c>AddModulusAuthorization</c> wires the <c>:</c>-policy convention
    /// (without it, requests hit an unresolvable-policy error instead of
    /// silently serving admin pages — call <c>AddModulusAuthorization</c> to
    /// fix it). Set to <c>null</c> or <c>""</c> to opt out of the permission
    /// check and keep only the authentication floor.
    /// </summary>
    public string? RequirePermission { get; set; } = AuditLoggingUiPermissions.View;

    /// <summary>Default page size for the browser. Defaults to 20.</summary>
    public int DefaultPageSize { get; set; } = 20;
}
