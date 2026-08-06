namespace ModulusSample.Modules.Identity.Application.Roles.Dtos;

public sealed record RoleResponse(
    Guid RoleId,
    string Name,
    string Description,
    bool IsSystem,
    int PermissionsCount);
