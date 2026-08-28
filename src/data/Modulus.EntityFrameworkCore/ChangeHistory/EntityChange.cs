namespace Modulus.EntityFrameworkCore.ChangeHistory;

/// <summary>
/// A recorded change to an entity: what property changed, from what value to
/// what value, who made the change, and when. Enables compliance auditing:
/// "who changed this invoice's amount?" (not just "who created/updated it").
///
/// Stored alongside the entity that was changed, allowing queries like:
/// "show me all changes to Invoices.Amount in the last 30 days."
/// </summary>
public sealed class EntityChange
{
    /// <summary>Unique identifier for this change record.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The entity that was changed (e.g., "Invoice", "PurchaseOrder").</summary>
    public string EntityName { get; init; } = default!;

    /// <summary>The primary key of the entity that was changed.</summary>
    public string EntityKey { get; init; } = default!;

    /// <summary>Tenant that owns this entity (multi-tenancy isolation).</summary>
    public Guid TenantId { get; init; }

    /// <summary>The property that changed (e.g., "Amount", "Status").</summary>
    public string PropertyName { get; init; } = default!;

    /// <summary>The value before the change (serialized as string; null if created).</summary>
    public string? OriginalValue { get; init; }

    /// <summary>The value after the change (serialized as string; null if deleted).</summary>
    public string? NewValue { get; init; }

    /// <summary>Who made the change.</summary>
    public string ChangedBy { get; init; } = default!;

    /// <summary>When the change was made.</summary>
    public DateTime ChangedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Optional tenant-specific correlation context (e.g., for audit trail grouping).</summary>
    public string? CorrelationId { get; init; }

    /// <summary>What operation triggered the change: "Create", "Update", "Delete".</summary>
    public string Operation { get; init; } = default!;
}
