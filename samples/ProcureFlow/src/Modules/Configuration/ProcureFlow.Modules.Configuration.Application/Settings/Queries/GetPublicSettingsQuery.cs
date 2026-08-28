using ProcureFlow.Modules.Configuration.Application.Settings.Dtos;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Application.Settings.Queries;

public sealed record GetPublicSettingsQuery() : Modulus.Mediator.Abstractions.IQuery<Result<List<PublicSettingResponse>>>;
