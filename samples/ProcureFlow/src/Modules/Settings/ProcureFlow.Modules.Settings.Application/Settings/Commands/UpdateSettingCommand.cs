using ModulusSample.Modules.Settings.Application.Settings.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Application.Settings.Commands;

public sealed record UpdateSettingCommand(
    Guid SettingId,
    string? Category,
    string? Description,
    bool? IsPublic) : Modulus.Mediator.Abstractions.ICommand<Result<UpdateSettingResponse>>;
