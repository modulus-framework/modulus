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
    /// folder. Defaults to <c>null</c> (pages open) so the UI works without
    /// the authorization stack; set to
    /// <see cref="AuditLoggingUiPermissions.View"/> once
    /// <c>AddModulusAuthorization</c> wires the <c>:</c>-policy convention.
    /// Declared on the registry by <c>AddModulusAuditLoggingUi</c>.
    /// </summary>
    public string? RequirePermission { get; set; }

    /// <summary>Default page size for the browser. Defaults to 20.</summary>
    public int DefaultPageSize { get; set; } = 20;
}
