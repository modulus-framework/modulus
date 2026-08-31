using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using TradeFlow.Modules.Configuration.Application.Settings.Dtos;
using TradeFlow.Modules.Configuration.Application.Settings.Queries;
using TradeFlow.Modules.Configuration.Domain.Entities;
using TradeFlow.Modules.Configuration.Domain.Repositories;
using TradeFlow.Shared.Application.Authorization;
using TradeFlow.Shared.Domain;

using TradeFlow.Modules.Configuration.Domain.ValueObjects;

namespace TradeFlow.Modules.Configuration.Application.Settings.Queries;

public sealed class GetSettingByIdQueryHandler(
    ISettingRepository repository) : IQueryHandler<GetSettingByIdQuery, Result<SettingResponse>>
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
            setting.UpdatedAt,
            setting.UpdatedBy);

        return Result.Success(response);
    }
}
