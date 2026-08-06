namespace ModulusSample.Modules.Identity.Application.Permissions.Dtos;

public sealed record MyPermissionsResponse(
    Guid UserId,
    string UserType,
    RoleDto? PrimaryRole,
    Dictionary<string, object> UserMetadata,
    List<RoleDto> Roles,
    List<string> Permissions);

public sealed record RoleDto(
    Guid Id,
    string Name,
    int Priority);
