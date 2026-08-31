using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Identity.Domain.ValueObjects;

/// <summary>
/// Represents a unique identifier for a permission.
/// </summary>
public sealed record PermissionId(string Value) : EntityId<string>(Value)
{
    public static PermissionId Create() => new(Guid.NewGuid().ToString());
    public static PermissionId Create(string value) => new(value);
    public static PermissionId Empty => new(string.Empty);
}
