namespace Meetup.Modules.UserAccess.Application.Dtos;

/// <summary>Read model for a system user.</summary>
public sealed record UserDto(Guid Id, string Login, string Email, bool IsActive, DateTime CreatedAt);
