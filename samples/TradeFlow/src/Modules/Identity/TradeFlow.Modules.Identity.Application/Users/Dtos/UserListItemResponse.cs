namespace TradeFlow.Modules.Identity.Application.Users.Dtos;

public sealed record UserListItemResponse(
    Guid UserId,
    string Email,
    string UserName,
    string FullName,
    string UserType,
    string Status,
    bool EmailConfirmed,
    DateTime CreatedAtUtc,
    string? ProfileImageUrl,
    DateTime? LastLoginAtUtc,
    List<string>? Roles = null);
