using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Application.Settings.Commands;

public sealed record BulkUpdateSettingsCommand(
    Dictionary<Guid, string> SettingUpdates) : Modulus.Mediator.Abstractions.ICommand<Result<int>>;