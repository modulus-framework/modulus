using Meetup.Modules.UserAccess.Application.Dtos;
using Meetup.Modules.UserAccess.Application.Queries.GetUsers;
using Meetup.Shared.Presentation;
using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.UserAccess.Presentation.Endpoints;

public sealed class GetUsersEndpoint(IMediator mediator)
    : EndpointWithoutRequest<IReadOnlyList<UserDto>>
{
    public override void Configure()
    {
        Get("/api/auth/users");
        Roles(MeetupRoles.Admins);
        Permissions(UserAccessPermissions.ViewUsers);
        Summary("Lists system users");
    }

    protected override async Task HandleAsync(CancellationToken ct)
    {
        var items = await mediator.QueryAsync(new GetUsersQuery(), ct);
        await SendOkAsync(items, ct);
    }
}
