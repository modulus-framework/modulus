using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Settings.Application.Abstractions;
using ModulusSample.Modules.Settings.Application.Settings.Commands;
using ModulusSample.Modules.Settings.Application.Settings.Dtos;
using ModulusSample.Modules.Settings.Domain.Entities;
using ModulusSample.Modules.Settings.Domain.Repositories;
using ModulusSample.Shared.Application.Authorization;
using ModulusSample.Shared.Domain;

using ModulusSample.Modules.Settings.Domain.ValueObjects;

namespace ModulusSample.Modules.Settings.Application.Settings.Commands;

public sealed class UpdateSettingCommandHandler(
    ISettingRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<UpdateSettingCommand, Result<UpdateSettingResponse>>
{
    public async Task<Result<UpdateSettingResponse>> HandleAsync(UpdateSettingCommand request, CancellationToken ct)
    {
        var settingId = SettingId.From(request.SettingId);
        var setting = await repository.GetByIdAsync(settingId, ct);
        if (setting is null)
        {
            return Result.Failure<UpdateSettingResponse>(Error.NotFound("Setting.NotFound", "Setting not found"));
        }

        var updateResult = setting.UpdateMetadata(
            request.Category ?? setting.Category,
            request.Description ?? setting.Description,
            request.IsPublic ?? setting.IsPublic,
            currentUser.UserId?.ToString() ?? "system");

        if (updateResult.IsFailure)
        {
            return Result.Failure<UpdateSettingResponse>(updateResult.Error);
        }

        await repository.UpdateAsync(setting, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new UpdateSettingResponse(setting.Id.Value, setting.Key.Value));
    }
}