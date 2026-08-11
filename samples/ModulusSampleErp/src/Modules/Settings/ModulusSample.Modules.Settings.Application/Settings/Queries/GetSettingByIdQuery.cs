using ModulusSample.Modules.Settings.Application.Settings.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Application.Settings.Queries;

public sealed record GetSettingByIdQuery(Guid SettingId) : Modulus.Mediator.Abstractions.IQuery<Result<SettingResponse>>;
