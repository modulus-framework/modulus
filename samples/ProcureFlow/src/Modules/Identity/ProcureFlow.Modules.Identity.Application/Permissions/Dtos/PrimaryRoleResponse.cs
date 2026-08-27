namespace ProcureFlow.Modules.Identity.Application.Permissions.Dtos;

public sealed record PrimaryRoleResponse(
    string PrimaryRoleName,
    int Priority,
    string RedirectUrl,
    string Reason);
