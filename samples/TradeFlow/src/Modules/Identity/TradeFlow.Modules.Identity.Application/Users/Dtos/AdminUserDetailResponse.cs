namespace TradeFlow.Modules.Identity.Application.Users.Dtos;

public sealed record AdminUserDetailResponse(
    Guid UserId,
    string Email,
    string UserName,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? ProfileImageUrl,
    string UserType,
    string Status,
    bool EmailConfirmed,
    bool PhoneNumberConfirmed,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc,
    List<AdminUserRoleDto> Roles);

public sealed record AdminUserRoleDto(
    Guid RoleId,
    string Name,
    string Description,
    bool IsSystem,
    DateTime AssignedAtUtc);
