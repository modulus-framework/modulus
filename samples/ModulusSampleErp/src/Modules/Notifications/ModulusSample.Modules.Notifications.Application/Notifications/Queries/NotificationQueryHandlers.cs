using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Notifications.Application.Notifications.Dtos;
using ModulusSample.Modules.Notifications.Application.Notifications.Queries;
using ModulusSample.Modules.Notifications.Domain.Constants;
using ModulusSample.Modules.Notifications.Domain.Entities;
using ModulusSample.Modules.Notifications.Domain.Repositories;
using ModulusSample.Modules.Notifications.Domain.ValueObjects;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Notifications.Application.Notifications.Queries;

public sealed class GetMyNotificationsHandler(
    INotificationRepository repository,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant) : IQueryHandler<GetMyNotificationsQuery, Result<PagedResult<NotificationResponse>>>
{
    public async Task<Result<PagedResult<NotificationResponse>>> HandleAsync(GetMyNotificationsQuery request, CancellationToken ct)
    {
        Guid userId = currentUser.UserId ?? Guid.Empty;
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;

        var paged = await repository.GetByUserAsync(
            userId,
            tenantId,
            request.IsRead,
            request.PageNumber,
            request.PageSize,
            ct);

        var responses = paged.Items.Select(ToResponse).ToList();

        return Result.Success(new PagedResult<NotificationResponse>(
            responses,
            paged.TotalCount,
            request.PageNumber,
            request.PageSize));
    }

    private static NotificationResponse ToResponse(Notification n) => new(
        n.Id.Value,
        n.RecipientUserId,
        n.Title,
        n.Message,
        n.Type,
        n.IsRead,
        n.ReadAtUtc,
        n.CreatedAt,
        n.CreatedBy,
        n.LastModifiedAt,
        n.LastModifiedBy);
}

public sealed class GetNotificationByIdHandler(
    INotificationRepository repository,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant) : IQueryHandler<GetNotificationByIdQuery, Result<NotificationResponse>>
{
    public async Task<Result<NotificationResponse>> HandleAsync(GetNotificationByIdQuery request, CancellationToken ct)
    {
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;

        var notification = await repository.GetByIdAsync(
            NotificationId.From(request.NotificationId),
            tenantId,
            ct);

        if (notification is null)
        {
            return Result.Failure<NotificationResponse>(NotificationErrors.NotFound);
        }

        if (notification.RecipientUserId != currentUser.UserId)
        {
            return Result.Failure<NotificationResponse>(NotificationErrors.NotOwnedByUser);
        }

        return Result.Success(ToResponse(notification));
    }

    private static NotificationResponse ToResponse(Notification n) => new(
        n.Id.Value,
        n.RecipientUserId,
        n.Title,
        n.Message,
        n.Type,
        n.IsRead,
        n.ReadAtUtc,
        n.CreatedAt,
        n.CreatedBy,
        n.LastModifiedAt,
        n.LastModifiedBy);
}

public sealed class GetUnreadCountHandler(
    INotificationRepository repository,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant) : IQueryHandler<GetUnreadCountQuery, Result<UnreadCountResponse>>
{
    public async Task<Result<UnreadCountResponse>> HandleAsync(GetUnreadCountQuery request, CancellationToken ct)
    {
        Guid userId = currentUser.UserId ?? Guid.Empty;
        Guid tenantId = currentTenant.TenantId ?? Guid.Empty;

        long unread = await repository.CountUnreadAsync(userId, tenantId, ct);
        return Result.Success(new UnreadCountResponse(unread));
    }
}
