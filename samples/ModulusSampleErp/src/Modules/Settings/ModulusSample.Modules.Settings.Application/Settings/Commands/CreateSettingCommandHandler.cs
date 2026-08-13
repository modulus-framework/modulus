using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ModulusSample.Modules.Settings.Application.Settings.Commands;
using ModulusSample.Modules.Settings.Application.Settings.Dtos;
using ModulusSample.Modules.Settings.Domain.Entities;
using ModulusSample.Modules.Settings.Domain.Repositories;
using ModulusSample.Modules.Settings.Domain.ValueObjects;
using ModulusSample.Shared.Application.Authorization;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Application.Settings.Commands;

public sealed class CreateSettingCommandHandler(
    ISettingRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<CreateSettingCommand, Result<CreateSettingResponse>>
{
    public async Task<Result<CreateSettingResponse>> HandleAsync(CreateSettingCommand request, CancellationToken ct)
    {
        var keyResult = SettingKey.Create(request.Key);
        if (keyResult.IsFailure)
        {
            return Result.Failure<CreateSettingResponse>(keyResult.Error);
        }

        var exists = await repository.ExistsByKeyAsync(keyResult.Value, request.TenantId, ct);
        if (exists)
        {
            return Result.Failure<CreateSettingResponse>(Error.Conflict("Setting.KeyExists", $"Setting with key '{request.Key}' already exists"));
        }

        var settingResult = Setting.Create(
            SettingId.Create(),
            keyResult.Value,
            request.Value,
            request.Category,
            request.Description,
            request.IsPublic,
            request.TenantId,
            currentUser.UserId?.ToString());

        if (settingResult.IsFailure)
        {
            return Result.Failure<CreateSettingResponse>(settingResult.Error);
        }

        await repository.AddAsync(settingResult.Value, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(new CreateSettingResponse(settingResult.Value.Id.Value, settingResult.Value.Key.Value));
    }
}
