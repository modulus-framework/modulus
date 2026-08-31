using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.EntityFrameworkCore.Abstractions;
using TradeFlow.Modules.Notifications.Application.Notifications.Commands;
using TradeFlow.Modules.Notifications.Application.Notifications.Dtos;
using TradeFlow.Modules.Notifications.Domain.Constants;
using TradeFlow.Modules.Notifications.Domain.Entities;
using TradeFlow.Modules.Notifications.Domain.Repositories;
using TradeFlow.Modules.Notifications.Domain.ValueObjects;
using TradeFlow.Shared.Application.Authorization;
using TradeFlow.Shared.Domain;

namespace TradeFlow.Modules.Notifications.Application.Notifications.Commands;

public sealed class CreateNotificationCommandHandler(
    INotificationRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<CreateNotificationCommand, Result<NotificationResponse>>
{
    public async Task<Result<NotificationResponse>> HandleAsync(CreateNotificationCommand request, CancellationToken ct)
    {
        var notificationResult = Notification.Create(
            NotificationId.Create(),
            request.RecipientUserId,
            request.Title,
            request.Message,
            request.Type,
            request.TenantId,
            currentUser.UserId?.ToString());

        if (notificationResult.IsFailure)
        {
            return Result.Failure<NotificationResponse>(notificationResult.Error);
        }

        await repository.AddAsync(notificationResult.Value, ct);
        await unitOfWork.CommitAsync(ct);

        return Result.Success(ToResponse(notificationResult.Value));
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

public sealed class MarkNotificationAsReadCommandHandler(
    INotificationRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<MarkNotificationAsReadCommand, Result<NotificationResponse>>
{
    public async Task<Result<NotificationResponse>> HandleAsync(MarkNotificationAsReadCommand request, CancellationToken ct)
    {
        var notification = await repository.GetByIdAsync(
            NotificationId.From(request.NotificationId),
            request.TenantId,
            ct);

        if (notification is null)
        {
            return Result.Failure<NotificationResponse>(NotificationErrors.NotFound);
        }

        if (notification.RecipientUserId != currentUser.UserId)
        {
            return Result.Failure<NotificationResponse>(NotificationErrors.NotOwnedByUser);
        }

        var markResult = notification.MarkAsRead(currentUser.UserId?.ToString() ?? "system");
        if (markResult.IsFailure)
        {
            return Result.Failure<NotificationResponse>(markResult.Error);
        }

        await repository.UpdateAsync(notification, ct);
        await unitOfWork.CommitAsync(ct);

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

public sealed class MarkAllNotificationsAsReadCommandHandler(
    INotificationRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : ICommandHandler<MarkAllNotificationsAsReadCommand, Result<MarkAllReadResponse>>
{
    public async Task<Result<MarkAllReadResponse>> HandleAsync(MarkAllNotificationsAsReadCommand request, CancellationToken ct)
    {
        Guid userId = currentUser.UserId ?? Guid.Empty;
        var paged = await repository.GetByUserAsync(userId, request.TenantId, false, 1, int.MaxValue, ct);
        var unread = paged.Items.ToList();

        foreach (Notification notification in unread)
        {
            notification.MarkAsRead(currentUser.UserId?.ToString() ?? "system");
            await repository.UpdateAsync(notification, ct);
        }

        await unitOfWork.CommitAsync(ct);

        return Result.Success(new MarkAllReadResponse(unread.Count));
    }
}
