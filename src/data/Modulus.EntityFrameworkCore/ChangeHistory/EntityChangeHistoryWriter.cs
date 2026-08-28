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
internal sealed class EntityChangeHistoryWriter : IEntityChangeHistoryWriter
{
    private readonly DbContext _dbContext;
    private readonly ICurrentTenant? _currentTenant;

    public EntityChangeHistoryWriter(DbContext dbContext, ICurrentTenant? currentTenant)
    {
        _dbContext = dbContext;
        _currentTenant = currentTenant;
    }

    public void CaptureChanges(
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

            var properties = entry.Entity.GetType().GetProperties();
            foreach (var property in properties)
            {
                // Check if the property or class is marked [Audited]
                var classAudited = entry.Entity.GetType()
                    .GetCustomAttributes(typeof(AuditedAttribute), false)
                    .Any();
                var propertyAudited = property
                    .GetCustomAttributes(typeof(AuditedAttribute), false)
                    .Any();

                if (!classAudited && !propertyAudited)
                    continue;

                // Skip audit fields themselves (CreatedBy, UpdatedAt, etc.)
                if (IsAuditField(property.Name))
                    continue;

                // Capture the change
                var originalValue = entry.State switch
                {
                    EntityState.Added => null,
                    EntityState.Deleted => Serialize(entry.OriginalValues[property.Name]),
                    EntityState.Modified when entry.OriginalValues[property.Name]
                        != entry.CurrentValues[property.Name]
                        => Serialize(entry.OriginalValues[property.Name]),
                    _ => null,
                };

                var newValue = entry.State switch
                {
                    EntityState.Deleted => null,
                    EntityState.Added or EntityState.Modified
                        => Serialize(entry.CurrentValues[property.Name]),
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
            _dbContext.Set<EntityChange>().AddRange(changes);
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
