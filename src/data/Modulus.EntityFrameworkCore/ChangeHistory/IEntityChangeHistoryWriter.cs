using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Modulus.EntityFrameworkCore.ChangeHistory;

/// <summary>
/// Captures field-level changes to audited entities as they're saved,
/// enabling compliance queries like "who changed this invoice's amount?"
/// </summary>
public interface IEntityChangeHistoryWriter
{
    /// <summary>
    /// Examines all changed entries and writes <see cref="EntityChange"/> records
    /// for any properties marked with <see cref="AuditedAttribute"/>.
    /// </summary>
    void CaptureChanges(
        IEnumerable<EntityEntry> entries,
        string changedBy,
        string? correlationId = null);
}
