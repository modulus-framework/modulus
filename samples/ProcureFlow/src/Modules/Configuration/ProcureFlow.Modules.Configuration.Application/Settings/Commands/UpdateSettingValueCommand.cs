using ProcureFlow.Modules.Configuration.Application.Settings.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Application.Settings.Commands;

public sealed record UpdateSettingValueCommand(
    Guid SettingId,
    string NewValue) : Modulus.Mediator.Abstractions.ICommand<Result<UpdateSettingResponse>>;
