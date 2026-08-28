using ProcureFlow.Modules.Configuration.Application.Settings.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Application.Settings.Queries;

public sealed record GetSettingByKeyQuery(string Key) : Modulus.Mediator.Abstractions.IQuery<Result<SettingResponse>>;
