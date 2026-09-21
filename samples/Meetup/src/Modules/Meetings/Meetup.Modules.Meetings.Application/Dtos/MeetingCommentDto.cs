namespace Meetup.Modules.Meetings.Application.Dtos;

public sealed record MeetingCommentDto(
    Guid Id,
    Guid MeetingId,
    string AuthorLogin,
    string Text,
    Guid? ReplyToId,
    DateTime CreatedAt);
