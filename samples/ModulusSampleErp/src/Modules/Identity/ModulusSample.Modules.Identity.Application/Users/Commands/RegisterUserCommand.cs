using ModulusSample.Modules.Identity.Application.Users.Dtos;

using ModulusSample.Shared.Domain;
namespace ModulusSample.Modules.Identity.Application.Users.Commands;

public sealed record RegisterUserCommand(
    string Email,
    string Password,
    string UserName,
    string FirstName,
    string LastName,
    string? PhoneNumber = null) : Modulus.Mediator.Abstractions.ICommand<Result<RegisterUserResponse>>;
