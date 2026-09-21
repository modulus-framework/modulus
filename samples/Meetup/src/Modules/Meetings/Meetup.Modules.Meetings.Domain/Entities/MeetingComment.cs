using Modulus.Core.Abstractions.Domain;

namespace Meetup.Modules.Meetings.Domain.Entities;

/// <summary>Member comment on a meeting (supports one-level replies).</summary>
public sealed class MeetingComment : AggregateRoot<Guid>
{
    public Guid MeetingId { get; private set; }
    public string AuthorLogin { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public Guid? ReplyToId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private MeetingComment()
    {
    }

    public static MeetingComment Add(Guid meetingId, string authorLogin, string text, Guid? replyToId = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Comment text is required.", nameof(text));
        }

        return new MeetingComment
        {
            Id = Guid.NewGuid(),
            MeetingId = meetingId,
            AuthorLogin = authorLogin,
            Text = text.Trim(),
            ReplyToId = replyToId,
            CreatedAt = DateTime.UtcNow
        };
    }
}
