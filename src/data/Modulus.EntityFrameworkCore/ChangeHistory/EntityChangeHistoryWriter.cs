using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Entities;

namespace Modulus.EntityFrameworkCore.ChangeHistory;

/// <summary>
/// Default implementation of <see cref="IEntityChangeHistoryWriter"/>.
/// Reflects over entities to find <see cref="AuditedAttribute"/> markers
/// and captures changes to those properties as <see cref="EntityChange"/> rows.
/// </summary>
internal sealed class EntityChangeHistoryWriter(ICurrentTenant? currentTenant)
    : IEntityChangeHistoryWriter
{
    private readonly ICurrentTenant? _currentTenant = currentTenant;

    public void CaptureChanges(
        DbContext context,
        IEnumerable<EntityEntry> entries,
        string changedBy,
        string? correlationId = null)
    {
        var changes = new List<EntityChange>();

        foreach (var entry in entries.Where(e => e.Entity is IAuditableEntity))
        {
            var entityName = entry.Entity.GetType().Name;
            var entityKey = GetEntityKey(entry);
            var auditableEntity = (IAuditableEntity)entry.Entity;
            var tenantId = _currentTenant?.TenantId ?? Guid.Empty;

            var operation = entry.State switch
            {
                EntityState.Added => "Create",
                EntityState.Modified => "Update",
                EntityState.Deleted => "Delete",
                _ => "Unknown",
            };

            // Class-level [Audited] is computed once per entry (it cannot vary
            // per property).
            var classAudited = entry.Entity.GetType()
                .GetCustomAttributes(typeof(AuditedAttribute), false)
                .Any();

            // Enumerate MAPPED SCALAR properties only. Reflecting over CLR
            // properties would also hit navigations and [NotMapped] members,
            // and reading OriginalValues for a non-scalar throws — failing
            // every SaveChanges of a class-audited entity with navigations.
            foreach (var property in entry.Metadata.GetProperties())
            {
                // Check if the property or class is marked [Audited]
                var propertyAudited = property.PropertyInfo?
                    .GetCustomAttributes(typeof(AuditedAttribute), false)
                    .Any() == true;

                if (!classAudited && !propertyAudited)
                    continue;

                // Skip audit fields themselves (CreatedBy, UpdatedAt, etc.)
                if (IsAuditField(property.Name))
                    continue;

                var propertyEntry = entry.Property(property.Name);

                // Capture the change
                var originalValue = entry.State switch
                {
                    EntityState.Added => null,
                    EntityState.Deleted => Serialize(propertyEntry.OriginalValue),
                    EntityState.Modified when !Equals(
                        propertyEntry.OriginalValue, propertyEntry.CurrentValue)
                        => Serialize(propertyEntry.OriginalValue),
                    _ => null,
                };

                var newValue = entry.State switch
                {
                    EntityState.Deleted => null,
                    EntityState.Added or EntityState.Modified
                        => Serialize(propertyEntry.CurrentValue),
                    _ => null,
                };

                // Only record if the value actually changed
                if (originalValue == newValue && entry.State != EntityState.Deleted)
                    continue;

                changes.Add(new EntityChange
                {
                    EntityName = entityName,
                    EntityKey = entityKey,
                    TenantId = tenantId,
                    PropertyName = property.Name,
                    OriginalValue = originalValue,
                    NewValue = newValue,
                    ChangedBy = changedBy,
                    ChangedAt = DateTime.UtcNow,
                    CorrelationId = correlationId,
                    Operation = operation,
                });
            }
        }

        if (changes.Count > 0)
            context.Set<EntityChange>().AddRange(changes);
    }

    private static string GetEntityKey(EntityEntry entry)
    {
        var keyProperties = entry.Metadata.FindPrimaryKey()?.Properties;
        if (keyProperties is null || keyProperties.Count == 0)
            return entry.Entity.GetHashCode().ToString();

        var keyParts = keyProperties
            .Select(p => entry.CurrentValues[p.Name]?.ToString() ?? "null")
            .ToArray();

        return string.Join("|", keyParts);
    }

    private static string? Serialize(object? value) =>
        value switch
        {
            null => null,
            string s => s,
            Guid g => g.ToString("N"),
            bool b => b.ToString(),
            int i => i.ToString(),
            long l => l.ToString(),
            decimal d => d.ToString(),
            DateTime dt => dt.ToString("O"),
            _ => value.ToString(),
        };

    private static bool IsAuditField(string name) =>
        name is "CreatedAt" or "CreatedBy" or "UpdatedAt" or "UpdatedBy"
            or "DeletedAt" or "DeletedBy" or "IsDeleted"
            or "Id" or "TenantId";
}
