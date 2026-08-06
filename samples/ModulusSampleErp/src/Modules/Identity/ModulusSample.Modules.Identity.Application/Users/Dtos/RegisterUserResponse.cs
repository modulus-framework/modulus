namespace ModulusSample.Modules.Identity.Application.Users.Dtos;

public sealed record RegisterUserResponse(
    Guid UserId,
    string Email,
    string UserName,
    string Status,
    string Message);
