namespace Modulus.Notifications;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modulus.Core.Abstractions.Exceptions;

/// <summary>One notification in the inbox response.</summary>
public sealed record NotificationResponse(Guid Id, string? Category, string Title, string? Message, NotificationSeverity Severity, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

/// <summary>A page of the inbox, with the unread count.</summary>
public sealed record NotificationPageResponse(IReadOnlyList<NotificationResponse> Items, int Total, int Unread, int Page, int PageSize);

/// <summary>A channel choice for one category.</summary>
public sealed record NotificationPreferenceResponse(string Category, string Channel, bool Enabled);

/// <summary>Body of <c>PUT preferences/{category}/{channel}</c>.</summary>
public sealed record NotificationPreferenceRequest(bool Enabled);

/// <summary>
/// The signed-in person's inbox over HTTP. Every route needs a signed-in person and acts only on that person's notifications in their
/// company, so no route takes a person id. An anonymous caller gets 401.
/// </summary>
public static class NotificationEndpoints
{
    /// <summary>Maps the inbox and preference routes under <paramref name="prefix"/> (default <c>/api/notifications</c>).</summary>
    public static RouteGroupBuilder MapModulusNotifications(this IEndpointRouteBuilder endpoints, string prefix = "/api/notifications")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var group = endpoints.MapGroup(prefix).RequireAuthorization();

        group.MapGet("", (bool? unreadOnly, int? page, int? pageSize, INotificationInbox inbox, CancellationToken ct) => Answer(async () =>
        {
            var size = Math.Clamp(pageSize ?? 20, 1, 200);
            var current = Math.Max(1, page ?? 1);
            var list = await inbox.ListAsync(unreadOnly ?? false, current, size, ct).ConfigureAwait(false);
            var unread = await inbox.UnreadCountAsync(ct).ConfigureAwait(false);
            var items = list.Items.Select(ToResponse).ToList();
            return Results.Ok(new NotificationPageResponse(items, list.TotalCount, unread, current, size));
        }));

        group.MapGet("/unread-count", (INotificationInbox inbox, CancellationToken ct) => Answer(async () =>
            Results.Ok(new { unread = await inbox.UnreadCountAsync(ct).ConfigureAwait(false) })));

        group.MapPost("/{id:guid}/read", (Guid id, INotificationInbox inbox, CancellationToken ct) => Answer(async () =>
        {
            await inbox.MarkReadAsync(id, ct).ConfigureAwait(false);
            return Results.NoContent();
        }));

        group.MapPost("/read-all", (INotificationInbox inbox, CancellationToken ct) => Answer(async () =>
            Results.Ok(new { changed = await inbox.MarkAllReadAsync(ct).ConfigureAwait(false) })));

        group.MapGet("/preferences/{category}/{channel}", (string category, string channel, INotificationInbox inbox, CancellationToken ct) => Answer(async () =>
        {
            var parsed = ParseChannel(channel);
            var enabled = await inbox.GetPreferenceAsync(category, parsed, ct).ConfigureAwait(false);
            return Results.Ok(new NotificationPreferenceResponse(category, parsed.ToString(), enabled));
        }));

        group.MapPut("/preferences/{category}/{channel}", (string category, string channel, NotificationPreferenceRequest body, INotificationInbox inbox, CancellationToken ct) => Answer(async () =>
        {
            await inbox.SetPreferenceAsync(category, ParseChannel(channel), body.Enabled, ct).ConfigureAwait(false);
            return Results.NoContent();
        }));

        return group;
    }

    private static NotificationResponse ToResponse(UserNotification n)
        => new(n.Id, n.Category, n.Title, n.Message, n.Severity, n.CreatedAt, n.ReadAt);

    // Only channels the framework delivers are accepted; an unknown name is a client error, not a silent default.
    private static NotificationChannel ParseChannel(string channel)
        => Enum.TryParse<NotificationChannel>(channel, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ValidationException([$"'{channel}' is not a notification channel that is delivered."]);

    private static async Task<IResult> Answer(Func<Task<IResult>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (ValidationException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["notification"] = ex.Errors.ToArray() });
        }
        catch (NotFoundException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (UnauthorizedException)
        {
            return Results.Unauthorized();
        }
    }
}
