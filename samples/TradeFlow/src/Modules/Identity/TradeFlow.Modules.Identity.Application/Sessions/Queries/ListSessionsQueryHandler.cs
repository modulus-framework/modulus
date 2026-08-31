using TradeFlow.Modules.Identity.Application.Abstractions.Authentication;
using TradeFlow.Modules.Identity.Application.Sessions.Dtos;
using TradeFlow.Modules.Identity.Application.Sessions.Queries;
using TradeFlow.Shared.Application.Abstractions.Oidc;
using TradeFlow.Shared.Domain;
using System.Text.Json;

namespace TradeFlow.Modules.Identity.Application.Sessions.Queries;

internal sealed class ListSessionsQueryHandler(
    ISessionService sessionService,
    IUserContext userContext)
    : Modulus.Mediator.Abstractions.IQueryHandler<ListSessionsQuery, Result<List<SessionDto>>>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<Result<List<SessionDto>>> HandleAsync(
        ListSessionsQuery request,
        CancellationToken cancellationToken)
    {
        Result<List<SessionInfo>> result = await sessionService.GetActiveSessionsAsync(
            userContext.UserId,
            cancellationToken);

        if (result.IsFailure)
        {
            return Result.Failure<List<SessionDto>>(result.Error);
        }

        var sessions = result.Value.Select(s =>
        {
            DeviceInfoDto deviceInfo = JsonSerializer.Deserialize<DeviceInfoDto>(s.DeviceInfoJson, JsonOptions) ?? new DeviceInfoDto(
                "Unknown", null, "Unknown", null, "Unknown");

            return new SessionDto(
                s.Id,
                deviceInfo,
                s.IpAddress,
                s.LoginTimeUtc,
                s.LastActivityTimeUtc,
                s.ExpiresAtUtc,
                s.IsCurrent);
        }).ToList();

        // Mark current session
        if (!string.IsNullOrEmpty(userContext.ExternalSessionId))
        {
            Result<SessionInfo> currentSessionResult = await sessionService.GetSessionByExternalSessionIdAsync(
                userContext.ExternalSessionId,
                cancellationToken);

            if (currentSessionResult.IsSuccess)
            {
                Guid currentDbSessionId = currentSessionResult.Value.Id;
                for (int i = 0; i < sessions.Count; i++)
                {
                    if (sessions[i].Id == currentDbSessionId)
                    {
                        sessions[i] = new SessionDto(
                            sessions[i].Id,
                            sessions[i].DeviceInfo,
                            sessions[i].IpAddress,
                            sessions[i].LoginTimeUtc,
                            sessions[i].LastActivityTimeUtc,
                            sessions[i].ExpiresAtUtc,
                            IsCurrent: true);
                        break;
                    }
                }
            }
        }

        return Result.Success(sessions);
    }
}
