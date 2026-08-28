using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Application.Settings.Commands;

public sealed record BulkUpdateSettingsCommand(
    Dictionary<Guid, string> SettingUpdates) : Modulus.Mediator.Abstractions.ICommand<Result<int>>;
