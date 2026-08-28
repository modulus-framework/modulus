using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Configuration.Application.Settings.Dtos;
using ProcureFlow.Modules.Configuration.Application.Settings.Queries;
using ProcureFlow.Modules.Configuration.Domain.Entities;
using ProcureFlow.Modules.Configuration.Domain.Repositories;
using ProcureFlow.Modules.Configuration.Domain.ValueObjects;
using ProcureFlow.Shared.Application.Authorization;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Application.Settings.Queries;

public sealed class GetSettingByKeyQueryHandler(
    ISettingRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetSettingByKeyQuery, Result<SettingResponse>>
{
    public async Task<Result<SettingResponse>> HandleAsync(GetSettingByKeyQuery request, CancellationToken ct)
    {
        var keyResult = SettingKey.Create(request.Key);
        if (keyResult.IsFailure)
        {
            return Result.Failure<SettingResponse>(keyResult.Error);
        }

        var setting = await repository.GetByKeyAsync(keyResult.Value, currentTenant.TenantId ?? Guid.Empty, ct);
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
