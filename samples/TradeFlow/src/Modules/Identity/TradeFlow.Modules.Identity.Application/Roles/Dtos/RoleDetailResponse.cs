namespace TradeFlow.Modules.Identity.Application.Roles.Dtos;

public sealed record RoleDetailResponse(
    Guid RoleId,
    string Name,
    string Description,
    bool IsSystem,
    List<string> Permissions,
    DateTime CreatedAtUtc);
