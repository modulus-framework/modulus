using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using TradeFlow.Modules.Configuration.Application.Settings.Commands;
using TradeFlow.Modules.Configuration.Domain.Entities;
using TradeFlow.Modules.Configuration.Domain.Repositories;
using TradeFlow.Shared.Application.Authorization;
using TradeFlow.Shared.Domain;

using TradeFlow.Modules.Configuration.Domain.ValueObjects;

namespace TradeFlow.Modules.Configuration.Application.Settings.Commands;

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
        await unitOfWork.CommitAsync(ct);

        return Result.Success();
    }
}
