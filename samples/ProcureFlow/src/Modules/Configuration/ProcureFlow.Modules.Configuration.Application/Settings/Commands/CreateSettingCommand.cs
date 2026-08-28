using ProcureFlow.Modules.Configuration.Application.Settings.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Application.Settings.Commands;

public sealed record CreateSettingCommand(
    string Key,
    string Value,
    string Category,
    string Description,
    bool IsPublic,
    Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result<CreateSettingResponse>>;
