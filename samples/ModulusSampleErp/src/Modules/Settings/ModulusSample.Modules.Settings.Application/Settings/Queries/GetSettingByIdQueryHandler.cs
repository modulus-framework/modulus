using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Settings.Application.Settings.Dtos;
using ModulusSample.Modules.Settings.Application.Settings.Queries;
using ModulusSample.Modules.Settings.Domain.Entities;
using ModulusSample.Modules.Settings.Domain.Repositories;
using ModulusSample.Shared.Application.Authorization;
using ModulusSample.Shared.Domain;

using ModulusSample.Modules.Settings.Domain.ValueObjects;

namespace ModulusSample.Modules.Settings.Application.Settings.Queries;

public sealed class GetSettingByIdQueryHandler(
    ISettingRepository repository,
    ICurrentUser currentUser) : IQueryHandler<GetSettingByIdQuery, Result<SettingResponse>>
{
    public async Task<Result<SettingResponse>> HandleAsync(GetSettingByIdQuery request, CancellationToken ct)
    {
        var settingId = SettingId.From(request.SettingId);
        var setting = await repository.GetByIdAsync(settingId, ct);
        if (setting is null)
        {
            return Result.Failure<SettingResponse>(Error.NotFound("Setting.NotFound", "Setting not found"));
        }

        var response = new SettingResponse(
            setting.Id.Value,
            setting.Key.Value,
            setting.Value,
            setting.Category,
            setting.Description,
            setting.IsPublic,
            setting.TenantId,
            setting.CreatedAt,
            setting.CreatedBy,
            setting.LastModifiedAt,
            setting.LastModifiedBy);

        return Result.Success(response);
    }
}