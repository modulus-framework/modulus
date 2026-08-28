using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ProcureFlow.Modules.Configuration.Application.Settings.Dtos;
using ProcureFlow.Modules.Configuration.Application.Settings.Queries;
using ProcureFlow.Modules.Configuration.Domain.Entities;
using ProcureFlow.Modules.Configuration.Domain.Repositories;
using ProcureFlow.Shared.Application.Authorization;
using ProcureFlow.Shared.Domain;

namespace ProcureFlow.Modules.Configuration.Application.Settings.Queries;

public sealed class GetPublicSettingsQueryHandler(
    ISettingRepository repository,
    ICurrentTenant currentTenant) : IQueryHandler<GetPublicSettingsQuery, Result<List<PublicSettingResponse>>>
{
    public async Task<Result<List<PublicSettingResponse>>> HandleAsync(GetPublicSettingsQuery request, CancellationToken ct)
    {
        var settings = await repository.GetPublicSettingsAsync(currentTenant.TenantId ?? Guid.Empty, ct);

        var responses = settings.Select(s => new PublicSettingResponse(
            s.Key.Value,
            s.Value,
            s.Category)).ToList();

        return Result.Success(responses);
    }
}
