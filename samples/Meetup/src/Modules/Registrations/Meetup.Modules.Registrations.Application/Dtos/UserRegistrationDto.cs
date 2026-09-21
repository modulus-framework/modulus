namespace Meetup.Modules.Registrations.Application.Dtos;

/// <summary>Read model for a user registration.</summary>
public sealed record UserRegistrationDto(
    Guid Id,
    string Login,
    string Email,
    string FirstName,
    string LastName,
    string Status,
    DateTime RegisteredAt,
    DateTime? ConfirmedAt);
