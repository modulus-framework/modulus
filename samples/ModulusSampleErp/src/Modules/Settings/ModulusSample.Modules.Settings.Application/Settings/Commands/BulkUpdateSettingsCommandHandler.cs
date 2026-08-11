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

public sealed class BulkUpdateSettingsCommandHandler(
    ISettingRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<BulkUpdateSettingsCommand, Result<int>>
{
    public async Task<Result<int>> HandleAsync(BulkUpdateSettingsCommand request, CancellationToken ct)
    {
        int updatedCount = 0;
        var settingIds = request.SettingUpdates.Keys.Select(id => SettingId.From(id)).ToList();

        foreach (var settingId in settingIds)
        {
            var setting = await repository.GetByIdAsync(settingId, ct);
            if (setting is null)
            {
                continue;
            }

            if (request.SettingUpdates.TryGetValue(settingId.Value, out var newValue))
            {
                var updateResult = setting.UpdateValue(newValue, currentUser.UserId?.ToString() ?? "system");
                if (updateResult.IsSuccess)
                {
                    await repository.UpdateAsync(setting, ct);
                    updatedCount++;
                }
            }
        }

        if (updatedCount > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return Result.Success(updatedCount);
    }
}
