using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Identity.Domain.ValueObjects;

/// <summary>
/// Represents the unique identifier for an email verification token.
/// </summary>
public sealed record TokenId(Guid Value) : EntityId<Guid>(Value)
{
    public static TokenId New() => new(Guid.NewGuid());

    public static TokenId Create(Guid value) => new(value);

    public static TokenId Empty => new(Guid.Empty);
}
