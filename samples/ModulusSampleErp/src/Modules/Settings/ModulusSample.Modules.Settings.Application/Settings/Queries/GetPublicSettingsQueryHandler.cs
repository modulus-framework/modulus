using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Settings.Application.Settings.Dtos;
using ModulusSample.Modules.Settings.Application.Settings.Queries;
using ModulusSample.Modules.Settings.Domain.Entities;
using ModulusSample.Modules.Settings.Domain.Repositories;
using ModulusSample.Shared.Application.Authorization;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Settings.Application.Settings.Queries;

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
