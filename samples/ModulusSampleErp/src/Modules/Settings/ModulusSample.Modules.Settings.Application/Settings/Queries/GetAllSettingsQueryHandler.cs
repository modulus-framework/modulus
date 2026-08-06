using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Settings.Application.Settings.Dtos;
using ModulusSample.Modules.Settings.Application.Settings.Queries;
using ModulusSample.Modules.Settings.Domain.Entities;
using ModulusSample.Modules.Settings.Domain.Repositories;
using ModulusSample.Shared.Application.Authorization;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Application.Settings.Queries;

public sealed class GetAllSettingsQueryHandler(
    ISettingRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetAllSettingsQuery, Result<PagedResult<SettingResponse>>>
{
    public async Task<Result<PagedResult<SettingResponse>>> HandleAsync(GetAllSettingsQuery request, CancellationToken ct)
    {
        var pagedResult = await repository.GetPagedAsync(
            currentTenant.TenantId ?? Guid.Empty,
            request.Category,
            null,
            request.IsPublic,
            request.PageNumber,
            request.PageSize,
            ct);

        var responses = pagedResult.Items.Select(s => new SettingResponse(
            s.Id.Value,
            s.Key.Value,
            s.Value,
            s.Category,
            s.Description,
            s.IsPublic,
            s.TenantId,
            s.CreatedAt,
            s.CreatedBy,
            s.LastModifiedAt,
            s.LastModifiedBy)).ToList();

        return Result.Success(new PagedResult<SettingResponse>(
            responses,
            pagedResult.TotalCount,
            request.PageNumber,
            request.PageSize));
    }
}