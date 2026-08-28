using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using ProcureFlow.Modules.Configuration.Application.Settings.Commands;
using ProcureFlow.Modules.Configuration.Application.Settings.Dtos;
using ProcureFlow.Modules.Configuration.Domain.Entities;
using ProcureFlow.Modules.Configuration.Domain.Repositories;
using ProcureFlow.Shared.Application.Authorization;
using ProcureFlow.Shared.Domain;

using ProcureFlow.Modules.Configuration.Domain.ValueObjects;

namespace ProcureFlow.Modules.Configuration.Application.Settings.Commands;

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
        await unitOfWork.CommitAsync(ct);

        return Result.Success(new UpdateSettingResponse(setting.Id.Value, setting.Key.Value));
    }
}
