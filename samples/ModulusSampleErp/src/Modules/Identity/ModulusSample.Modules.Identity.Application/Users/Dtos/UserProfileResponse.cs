namespace ModulusSample.Modules.Identity.Application.Users.Dtos;

public sealed record UserProfileResponse(
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
    DateTime? LastLoginAtUtc);
