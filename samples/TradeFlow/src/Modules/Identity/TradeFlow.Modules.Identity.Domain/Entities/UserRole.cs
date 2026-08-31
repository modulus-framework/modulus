using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Domain.ValueObjects;

namespace TradeFlow.Modules.Identity.Domain.Entities;

public sealed class UserRole
{
    private UserRole() { }

    private UserRole(UserId userId, RoleId roleId)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        RoleId = roleId;
        AssignedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public UserId UserId { get; private set; } = default!;
    public RoleId RoleId { get; private set; } = default!;
    public DateTime AssignedAtUtc { get; private set; }

    public static UserRole Create(UserId userId, RoleId roleId) => new(userId, roleId);
}
