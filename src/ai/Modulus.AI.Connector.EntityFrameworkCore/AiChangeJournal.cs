namespace Modulus.AI.Connector.EntityFrameworkCore;

using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Modulus.AI.Connector.Data;
using Modulus.Core.Abstractions.Ai;
using Modulus.Core.Abstractions.Entities;
using Modulus.EntityFrameworkCore.ModelBuilding;
using Modulus.EntityFrameworkCore.Saving;

/// <summary>
/// One row of the <c>ai_changes</c> journal: an <see cref="AiIndexedAttribute"/> entity was inserted, updated or deleted.
/// Written by the saving context in the same transaction as the entity, so a committed change is never missed.
/// </summary>
public sealed class AiChangeRecord
{
    /// <summary>The journal position within its context (store-generated, increasing).</summary>
    public long Sequence { get; set; }

    /// <summary>The company (<see cref="Guid.Empty"/> for rows saved without one).</summary>
    public Guid TenantId { get; set; }

    /// <summary>The resource type of the entity.</summary>
    public string ResourceType { get; set; } = "";

    /// <summary>The entity's key, as the resource lookup takes it.</summary>
    public string ResourceId { get; set; } = "";

    /// <summary>Upsert or delete.</summary>
    public AiChangeKind Kind { get; set; }

    /// <summary>When the change was saved (UTC).</summary>
    public DateTime OccurredAt { get; set; }
}

/// <summary>Maps <see cref="AiChangeRecord"/> into every module context (table <c>{prefix}ai_changes</c>).</summary>
internal sealed class AiChangeModelContributor : IModuleModelContributor
{
    public const string Table = "ai_changes";

    public void Contribute(ModelBuilder modelBuilder)
        => modelBuilder.Entity<AiChangeRecord>(b =>
        {
            b.ToTable(Table);
            b.HasKey(c => c.Sequence);
            b.Property(c => c.Sequence).ValueGeneratedOnAdd();
            b.Property(c => c.ResourceType).HasMaxLength(200).IsRequired();
            b.Property(c => c.ResourceId).HasMaxLength(200).IsRequired();
            b.Property(c => c.Kind).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(c => new { c.TenantId, c.Sequence });
            b.HasIndex(c => c.OccurredAt);
        });
}

/// <summary>
/// Journals every added, modified and deleted <see cref="AiIndexedAttribute"/> entity of the unit of work. A soft delete
/// is journaled as a delete. The key must be known before saving (client-generated, such as <see cref="Guid"/> keys):
/// a store-generated key has no value yet, and the save fails rather than journal a wrong id.
/// </summary>
internal sealed class AiChangeSaveContributor(TimeProvider time) : IModuleSaveContributor
{
    private static readonly ConcurrentDictionary<Type, string?> ResourceTypes = new();

    public void OnSaving(DbContext context, Guid? tenantId)
    {
        if (context.Model.FindEntityType(typeof(AiChangeRecord)) is null)
            return;

        var now = time.GetUtcNow().UtcDateTime;
        var records = new List<AiChangeRecord>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
                || ResourceTypes.GetOrAdd(entry.Metadata.ClrType, ResourceTypeOf) is not { } resourceType)
                continue;

            var deleted = entry.State == EntityState.Deleted || entry.Entity is ISoftDelete { IsDeleted: true };
            records.Add(new AiChangeRecord
            {
                TenantId = entry.Entity is IHasTenantId owned ? owned.TenantId : tenantId ?? Guid.Empty,
                ResourceType = resourceType,
                ResourceId = KeyOf(entry),
                Kind = deleted ? AiChangeKind.Delete : AiChangeKind.Upsert,
                OccurredAt = now,
            });
        }

        if (records.Count > 0)
            context.Set<AiChangeRecord>().AddRange(records);
    }

    private static string? ResourceTypeOf(Type type) => type.GetCustomAttribute<AiIndexedAttribute>()?.ResourceType;

    private static string KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey()?.Properties;
        if (key is not { Count: 1 })
            throw new InvalidOperationException($"[AiIndexed] '{entry.Metadata.ClrType.Name}' needs a single-column key.");

        var property = entry.Property(key[0].Name);
        if (property.IsTemporary)
        {
            throw new InvalidOperationException(
                $"[AiIndexed] '{entry.Metadata.ClrType.Name}' has a store-generated key, which is unknown until saved; " +
                "generate the key on the client (for example Guid.CreateVersion7()).");
        }

        return AiKeys.Format(property.CurrentValue);
    }
}

/// <summary>Formats keys the way the connector's resource lookup parses them.</summary>
internal static class AiKeys
{
    public static string Format(object? value)
        => value switch
        {
            Guid guid => guid.ToString("D"),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value?.ToString() ?? "",
        };

    public static object Parse(string value, Type type)
        => type == typeof(string) ? value
            : type == typeof(Guid) ? Guid.Parse(value)
            : Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
}
