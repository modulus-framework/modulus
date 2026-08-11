using ModulusSample.Modules.Notifications.Application.Notifications.Dtos;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Notifications.Application.Notifications.Queries;

public sealed record GetMyNotificationsQuery(
    bool? IsRead = null,
    int PageNumber = 1,
    int PageSize = 20) : Modulus.Mediator.Abstractions.IQuery<Result<PagedResult<NotificationResponse>>>;

public sealed record GetNotificationByIdQuery(
    Guid NotificationId) : Modulus.Mediator.Abstractions.IQuery<Result<NotificationResponse>>;

public sealed record GetUnreadCountQuery : Modulus.Mediator.Abstractions.IQuery<Result<UnreadCountResponse>>;
