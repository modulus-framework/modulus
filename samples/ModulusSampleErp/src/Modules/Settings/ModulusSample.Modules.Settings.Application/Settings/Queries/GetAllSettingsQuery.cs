using ModulusSample.Modules.Settings.Application.Settings.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Application.Settings.Queries;

public sealed record GetAllSettingsQuery(
    string? Category = null,
    bool? IsPublic = null,
    int PageNumber = 1,
    int PageSize = 20) : Modulus.Mediator.Abstractions.IQuery<Result<PagedResult<SettingResponse>>>;
