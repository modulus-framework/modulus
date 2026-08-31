using TradeFlow.Modules.Identity.Domain.ValueObjects;
using TradeFlow.Shared.Domain.ValueObjects;

namespace TradeFlow.Modules.Identity.Domain.Entities;

public sealed class RolePermission
{
    private RolePermission() { }

    private RolePermission(RoleId roleId, PermissionId permissionId, UserId grantedByUserId)
    {
        Id = Guid.NewGuid();
        RoleId = roleId;
        PermissionId = permissionId;
        GrantedByUserId = grantedByUserId;
        GrantedAtUtc = DateTime.UtcNow;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public RoleId RoleId { get; private set; } = default!;
    public PermissionId PermissionId { get; private set; } = default!;
    public UserId GrantedByUserId { get; private set; } = default!;
    public DateTime GrantedAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public UserId? RevokedByUserId { get; private set; }

    public static RolePermission Create(RoleId roleId, PermissionId permissionId, UserId grantedByUserId)
    {
        return new RolePermission(roleId, permissionId, grantedByUserId);
    }

    public void Revoke(UserId revokedByUserId)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        RevokedAtUtc = DateTime.UtcNow;
        RevokedByUserId = revokedByUserId;
    }

    public void Reactivate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        RevokedAtUtc = null;
        RevokedByUserId = null;
    }
}
