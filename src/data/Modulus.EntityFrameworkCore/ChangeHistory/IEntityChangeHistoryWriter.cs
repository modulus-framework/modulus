using Microsoft.EntityFrameworkCore;
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
    /// <param name="context">
    /// The context that is saving, and into whose <c>EntityChange</c> set the
    /// audit rows must be added. Passed explicitly rather than injected: in a
    /// modular monolith each module owns its own <c>DbContext</c>, and resolving
    /// a bare <see cref="DbContext"/> from DI would return the last-registered
    /// one (or fail outright), so audit rows would be attached to a context that
    /// is never saved and would be dropped without a trace.
    /// </param>
    /// <param name="entries">All change-tracked entries from <see cref="DbContext.ChangeTracker"/>.</param>
    /// <param name="changedBy">The identity performing the change (e.g. username or user ID).</param>
    /// <param name="correlationId">The correlation ID from the ambient request, if any.</param>
    void CaptureChanges(
        DbContext context,
        IEnumerable<EntityEntry> entries,
        string changedBy,
        string? correlationId = null);
}
