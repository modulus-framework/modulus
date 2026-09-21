using Meetup.Modules.Meetings.Application.Commands.AddComment;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.Meetings.Presentation.Endpoints;

public sealed class AddCommentRequest
{
    public Guid MeetingId { get; set; }
    public string AuthorLogin { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public Guid? ReplyToId { get; set; }
}

public sealed class AddCommentEndpoint(IMediator mediator) : Endpoint<AddCommentRequest, Guid>
{
    public override void Configure()
    {
        Post("/api/meetings/meetings/{meetingId}/comments");
        Permissions(MeetingsPermissions.Comment);
        Summary("Comments on a meeting");
    }

    public override async Task HandleAsync(AddCommentRequest req, CancellationToken ct)
    {
        var id = await mediator.SendAsync(
            new AddCommentCommand(req.MeetingId, req.AuthorLogin, req.Text, req.ReplyToId), ct);
        await SendCreatedAsync(id, $"/api/meetings/comments/{id}", ct);
    }
}
