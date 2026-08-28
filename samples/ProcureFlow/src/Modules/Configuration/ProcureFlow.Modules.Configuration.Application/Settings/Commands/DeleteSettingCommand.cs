using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Application.Settings.Commands;

public sealed record DeleteSettingCommand(Guid SettingId) : Modulus.Mediator.Abstractions.ICommand<Result>;
