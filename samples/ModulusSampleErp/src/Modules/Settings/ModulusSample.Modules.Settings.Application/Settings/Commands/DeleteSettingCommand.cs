using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Application.Settings.Commands;

public sealed record DeleteSettingCommand(Guid SettingId) : Modulus.Mediator.Abstractions.ICommand<Result>;