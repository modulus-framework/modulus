using Meetup.Modules.UserAccess.Application.Dtos;
using Meetup.Modules.UserAccess.Domain;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.UserAccess.Application.Queries.GetUsers;

public sealed class GetUsersHandler(IUserRepository repo)
    : IQueryHandler<GetUsersQuery, IReadOnlyList<UserDto>>
{
    public async Task<IReadOnlyList<UserDto>> HandleAsync(GetUsersQuery query, CancellationToken ct)
    {
        var items = await repo.GetAllAsync(ct);
        return items
            .Select(x => new UserDto(x.Id, x.Login, x.Email, x.IsActive, x.CreatedAt))
            .ToList();
    }
}
