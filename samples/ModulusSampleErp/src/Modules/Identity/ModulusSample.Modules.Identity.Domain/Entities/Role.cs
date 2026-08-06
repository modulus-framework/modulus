using ModulusSample.Modules.Identity.Domain.Events;
using ModulusSample.Modules.Identity.Domain.ValueObjects;
using ModulusSample.Shared.Domain;
using ModulusSample.Shared.Domain.ValueObjects;

namespace ModulusSample.Modules.Identity.Domain.Entities;

public sealed class Role : AggregateRoot
{
    private readonly List<RolePermission> _permissions = [];

    private Role() { }

    private Role(RoleId id, string name, string description, bool isSystem)
    {
        Id = id;
        Name = name;
        Description = description;
        IsSystem = isSystem;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public RoleId Id { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public bool IsSystem { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? LastReviewedAtUtc { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    public static Result<Role> Create(RoleId id, string name, string description, bool isSystem = false)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return Result.Failure<Role>(Error.Validation(
                "Role.InvalidName",
                "Role name must be between 1 and 100 characters"));
        }

        if (description != null && description.Length > 500)
        {
            return Result.Failure<Role>(Error.Validation(
                "Role.InvalidDescription",
                "Role description cannot exceed 500 characters"));
        }

        var role = new Role(id, name.Trim(), description?.Trim() ?? string.Empty, isSystem);
        role.Raise(new RoleCreatedDomainEvent(id, name));
        return Result.Success(role);
    }

    public Result UpdateDetails(string name, string description)
    {
        if (IsSystem)
        {
            return Result.Failure(Error.Validation(
                "Role.SystemRoleCannotBeModified",
                "System roles cannot be modified"));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation(
                "Role.NameRequired",
                "Role name is required"));
        }

        if (name.Length > 100)
        {
            return Result.Failure(Error.Validation(
                "Role.InvalidName",
                "Role name cannot exceed 100 characters"));
        }

        if (description != null && description.Length > 500)
        {
            return Result.Failure(Error.Validation(
                "Role.InvalidDescription",
                "Role description cannot exceed 500 characters"));
        }

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;

        return Result.Success();
    }

    public void AddPermission(PermissionId permissionId, UserId grantedByUserId)
    {
        if (_permissions.Any(rp => rp.PermissionId == permissionId && rp.IsActive))
        {
            return;
        }

        var rolePermission = RolePermission.Create(Id, permissionId, grantedByUserId);
        _permissions.Add(rolePermission);

        Raise(new PermissionAssignedToRoleDomainEvent(Id, permissionId, grantedByUserId, DateTime.UtcNow));
    }

    public void RemovePermission(PermissionId permissionId, UserId revokedByUserId)
    {
        RolePermission? rolePermission = _permissions
            .Find(rp => rp.PermissionId == permissionId && rp.IsActive);

        if (rolePermission != null)
        {
            rolePermission.Revoke(revokedByUserId);

            Raise(new PermissionRevokedFromRoleDomainEvent(Id, permissionId, revokedByUserId, DateTime.UtcNow));
        }
    }

    public bool HasPermission(PermissionId permissionId)
    {
        return _permissions.Any(rp => rp.PermissionId == permissionId && rp.IsActive);
    }

    public IEnumerable<RolePermission> GetActivePermissions()
    {
        return _permissions.Where(rp => rp.IsActive);
    }

    public IEnumerable<string> GetActivePermissionCodes()
    {
        return _permissions.Where(rp => rp.IsActive).Select(rp => rp.PermissionId.Value);
    }

    public IEnumerable<PermissionId> GetActivePermissionIds()
    {
        return _permissions.Where(rp => rp.IsActive).Select(rp => rp.PermissionId);
    }

    public void MarkReviewed()
    {
        LastReviewedAtUtc = DateTime.UtcNow;
    }
}
