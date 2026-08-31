using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Configuration.Application.Settings.Dtos;
using TradeFlow.Modules.Configuration.Application.Settings.Queries;
using TradeFlow.Modules.Configuration.Domain.Entities;
using TradeFlow.Modules.Configuration.Domain.Repositories;
using TradeFlow.Shared.Application.Authorization;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Configuration.Application.Settings.Queries;

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
            s.UpdatedAt,
            s.UpdatedBy)).ToList();

        return Result.Success(new PagedResult<SettingResponse>(
            responses,
            pagedResult.TotalCount,
            request.PageNumber,
            request.PageSize));
    }
}
