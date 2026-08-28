using ProcureFlow.Modules.Identity.Application.Users.Dtos;

using ProcureFlow.Shared.Domain;
namespace ProcureFlow.Modules.Identity.Application.Users.Commands;

public sealed record RegisterUserCommand(
    string Email,
    string Password,
    string UserName,
    string FirstName,
    string LastName,
    string? PhoneNumber = null) : Modulus.Mediator.Abstractions.ICommand<Result<RegisterUserResponse>>;
