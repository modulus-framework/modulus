using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Domain.ValueObjects;

public sealed record UserId(Guid Value) : EntityId<Guid>(Value)
{
    public static UserId Empty => new(Guid.Empty);
    public static UserId Create() => new(Guid.NewGuid());
    public static UserId Create(Guid value) => new(value);

    /// <summary>
    /// Implicit conversion from Guid to UserId.
    /// </summary>
    public static implicit operator UserId(Guid value) => new(value);

    /// <summary>
    /// Implicit conversion from UserId to Guid.
    /// </summary>
    public static implicit operator Guid(UserId id) => id.Value;
}
