using Meetup.Modules.Meetings.Domain.Entities;
using Meetup.Modules.Meetings.Domain.Repositories;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Application.Commands.AddComment;

public sealed class AddCommentHandler(
    IMeetingRepository meetings,
    IMeetingCommentRepository comments,
    IUnitOfWork unitOfWork)
    : ICommandHandler<AddCommentCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddCommentCommand command, CancellationToken ct)
    {
        var exists = await meetings.ExistsAsync(command.MeetingId, ct);
        if (!exists)
        {
            throw new KeyNotFoundException($"Meeting {command.MeetingId} not found.");
        }

        var comment = MeetingComment.Add(command.MeetingId, command.AuthorLogin, command.Text, command.ReplyToId);
        await comments.AddAsync(comment, ct);
        await unitOfWork.CommitAsync(ct);
        return comment.Id;
    }
}
