namespace Modulus.AI.Connector.EntityFrameworkCore;

using System.Reflection;
using Microsoft.Extensions.Compliance.Classification;
using Modulus.AI.Connector.Audit;
using Modulus.AI.Connector.Data;
using Modulus.Authorization.Fields;
using Modulus.Core.Abstractions.Ai;
using Modulus.Core.Abstractions.Compliance;
using Modulus.Core.Abstractions.Entities;
using Modulus.EntityFrameworkCore.ChangeHistory;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

/// <summary>
/// Lists the field-level change history (<c>[Audited]</c> entities) of one resource type, or one record of it, in the
/// caller's company, newest first. Only <see cref="AiIndexedAttribute"/> or <see cref="AiQueryableAttribute"/> entities
/// are reachable; values of secret fields, unknown properties and classified fields the caller may not read are null.
/// </summary>
[AiCapability(Name, "Lists who changed which field of a record (or of a resource type), from what to what, newest first.")]
[RequirePermission(AiAuditCapabilities.Permission)]
public sealed record ListEntityChanges(
    string ResourceType,
    string? ResourceId = null,
    string? Field = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int? Take = null) : IQuery<IReadOnlyList<EntityChangeRecord>>
{
    /// <summary>The capability name.</summary>
    public const string Name = "Modulus.Audit.EntityChange.List";
}

/// <summary>One field change, as the platform sees it.</summary>
public sealed record EntityChangeRecord
{
    /// <summary>The record's key.</summary>
    public string ResourceId { get; init; } = "";

    /// <summary><c>Create</c>, <c>Update</c> or <c>Delete</c>.</summary>
    public string Operation { get; init; } = "";

    /// <summary>The field.</summary>
    public string Field { get; init; } = "";

    /// <summary>The value before (null when hidden from the caller).</summary>
    public string? OldValue { get; init; }

    /// <summary>The value after (null when hidden from the caller).</summary>
    public string? NewValue { get; init; }

    /// <summary>Who changed it.</summary>
    [PersonalInformation]
    public string ChangedBy { get; init; } = "";

    /// <summary>When (UTC).</summary>
    public DateTimeOffset ChangedAt { get; init; }

    /// <summary>The request that made the change.</summary>
    public string? CorrelationId { get; init; }
}

internal sealed class ListEntityChangesHandler(
    IEntityChangeHistoryReader reader,
    IAiEntitySource source,
    IServiceProvider services) : IQueryHandler<ListEntityChanges, IReadOnlyList<EntityChangeRecord>>
{
    public async Task<IReadOnlyList<EntityChangeRecord>> HandleAsync(ListEntityChanges query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var entity = source.EntityTypes.FirstOrDefault(t =>
                t.GetCustomAttribute<AiIndexedAttribute>()?.ResourceType == query.ResourceType
                || t.GetCustomAttribute<AiQueryableAttribute>()?.ResourceType == query.ResourceType)
            ?? throw new ArgumentException($"No history for resource type '{query.ResourceType}'.");

        var changes = await reader.QueryAsync(
            new EntityChangeQuery
            {
                EntityName = entity.Name,
                EntityKey = query.ResourceId,
                PropertyName = query.Field,
                From = query.From,
                To = query.To,
                Take = AiAuditCapabilities.Take(query.Take),
            },
            ct);

        // Without a field authorizer every classified value stays hidden (fail closed).
        var mask = services.GetService<IFieldAuthorizer>()?.MaskFor(entity);
        return [.. changes.Select(c =>
        {
            var visible = Visible(entity, c.PropertyName, mask);
            return new EntityChangeRecord
            {
                ResourceId = c.EntityKey,
                Operation = c.Operation,
                Field = c.PropertyName,
                OldValue = visible ? c.OriginalValue : null,
                NewValue = visible ? c.NewValue : null,
                ChangedBy = c.ChangedBy,
                ChangedAt = new DateTimeOffset(DateTime.SpecifyKind(c.ChangedAt, DateTimeKind.Utc)),
                CorrelationId = c.CorrelationId,
            };
        })];
    }

    private static bool Visible(Type entity, string name, FieldMask? mask)
    {
        var property = entity.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property is null)
            return false;
        var classified = property.GetCustomAttributes<DataClassificationAttribute>(inherit: true).ToList();
        if (classified.Any(a => a is SecretDataAttribute))
            return false;

        // Field security ([Classified]) and the compliance taxonomy both guard a value; either needs the caller's mask.
        var guarded = classified.Count > 0
            || FieldClassificationMap.For(entity).GetValueOrDefault(property.Name) != FieldClassification.Public;
        return !guarded || (mask?.CanRead(property.Name) ?? false);
    }
}
