using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

namespace Meetup.Modules.Meetings.Application.Commands.AddComment;

/// <summary>Adds a comment to a meeting.</summary>
// Single-commit write on one module context: EF's implicit SaveChanges
// transaction plus the transactional outbox suffice. [Transactional] would
// couple Application to the Infrastructure DbContext type.
[SkipTransaction]
public sealed record AddCommentCommand(Guid MeetingId, string AuthorLogin, string Text, Guid? ReplyToId)
    : ICommand<Guid>;
