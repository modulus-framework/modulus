using ProcureFlow.Modules.Configuration.Application.Settings.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Application.Settings.Commands;

public sealed record UpdateSettingCommand(
    Guid SettingId,
    string? Category,
    string? Description,
    bool? IsPublic) : Modulus.Mediator.Abstractions.ICommand<Result<UpdateSettingResponse>>;
