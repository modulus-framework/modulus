namespace ProcureFlow.Modules.Identity.Application.Roles.Dtos;

/// <summary>
/// Response DTO containing current user's roles with full details
/// </summary>
public sealed record MyRolesResponse(
    Guid UserId,
    IEnumerable<RoleDetailInfo> Roles);

/// <summary>
/// Detailed role information including permissions
/// </summary>
public sealed record RoleDetailInfo(
    Guid RoleId,
    string Name,
    string Description,
    bool IsSystem,
    IEnumerable<string> Permissions);
