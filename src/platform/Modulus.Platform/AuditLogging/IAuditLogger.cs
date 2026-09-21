namespace Modulus.AuditLogging;

/// <summary>
/// Ambient-context audit writer: captures tenant/user/time so call sites
/// stay one line. Reads go straight to <see cref="IAuditLogStore"/>.
/// </summary>
public interface IAuditLogger
{
    Task LogAsync(
        string action,
        string? resource = null,
        string? resourceId = null,
        string? detail = null,
        CancellationToken ct = default);
}
