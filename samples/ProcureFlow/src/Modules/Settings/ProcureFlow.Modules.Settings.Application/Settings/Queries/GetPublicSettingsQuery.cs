using ModulusSample.Modules.Settings.Application.Settings.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Application.Settings.Queries;

public sealed record GetPublicSettingsQuery() : Modulus.Mediator.Abstractions.IQuery<Result<List<PublicSettingResponse>>>;
