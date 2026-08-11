using Modulus.Mediator.Abstractions;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Partners.Application.Commands;

public sealed record CreatePartnerCommand(
    string Name,
    string Type,
    string Email,
    string Phone,
    string Address) : ICommand<Result<Guid>>;
