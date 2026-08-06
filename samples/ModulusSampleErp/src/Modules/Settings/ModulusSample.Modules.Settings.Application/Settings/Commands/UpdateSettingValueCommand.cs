using ModulusSample.Modules.Settings.Application.Settings.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Application.Settings.Commands;

public sealed record UpdateSettingValueCommand(
    Guid SettingId,
    string NewValue) : Modulus.Mediator.Abstractions.ICommand<Result<UpdateSettingResponse>>;