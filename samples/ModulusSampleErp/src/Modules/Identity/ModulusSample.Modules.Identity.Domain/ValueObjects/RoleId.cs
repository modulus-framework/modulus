using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Domain.ValueObjects;

public sealed record RoleId(Guid Value) : EntityId<Guid>(Value)
{
    public static RoleId Create() => new(Guid.NewGuid());
    public static RoleId Create(Guid value) => new(value);
    public static RoleId Empty => new(Guid.Empty);
}
