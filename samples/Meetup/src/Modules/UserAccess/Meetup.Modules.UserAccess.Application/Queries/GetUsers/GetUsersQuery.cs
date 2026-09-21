using Meetup.Modules.UserAccess.Application.Dtos;
using Modulus.Mediator.Abstractions;

namespace Meetup.Modules.UserAccess.Application.Queries.GetUsers;

public sealed record GetUsersQuery : IQuery<IReadOnlyList<UserDto>>;
