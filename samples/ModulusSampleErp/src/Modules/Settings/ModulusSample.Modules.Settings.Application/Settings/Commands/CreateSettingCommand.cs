using ModulusSample.Modules.Settings.Application.Settings.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Application.Settings.Commands;

public sealed record CreateSettingCommand(
    string Key,
    string Value,
    string Category,
    string Description,
    bool IsPublic,
    Guid TenantId) : Modulus.Mediator.Abstractions.ICommand<Result<CreateSettingResponse>>;
