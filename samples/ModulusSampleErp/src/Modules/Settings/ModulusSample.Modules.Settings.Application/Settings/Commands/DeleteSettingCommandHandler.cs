using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Settings.Application.Abstractions;
using ModulusSample.Modules.Settings.Application.Settings.Commands;
using ModulusSample.Modules.Settings.Domain.Entities;
using ModulusSample.Modules.Settings.Domain.Repositories;
using ModulusSample.Shared.Application.Authorization;
using ModulusSample.Shared.Domain;

using ModulusSample.Modules.Settings.Domain.ValueObjects;

namespace ModulusSample.Modules.Settings.Application.Settings.Commands;

public sealed class DeleteSettingCommandHandler(
    ISettingRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<DeleteSettingCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteSettingCommand request, CancellationToken ct)
    {
        var settingId = SettingId.From(request.SettingId);
        var setting = await repository.GetByIdAsync(settingId, ct);
        if (setting is null)
        {
            return Result.Failure(Error.NotFound("Setting.NotFound", "Setting not found"));
        }

        setting.Delete(currentUser.UserId?.ToString() ?? "system");
        await repository.DeleteAsync(setting, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}