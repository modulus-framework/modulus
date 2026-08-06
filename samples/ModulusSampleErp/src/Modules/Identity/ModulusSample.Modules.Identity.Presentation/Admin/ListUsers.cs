using Modulus.AspNetCore.Endpoints;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Identity.Application.Users.Dtos;
using ModulusSample.Modules.Identity.Application.Users.Queries;
using ModulusSample.Shared.Domain;

namespace ModulusSample.Modules.Identity.Presentation.Admin;

internal sealed class ListUsersEndpoint : Endpoint<ListUsersEndpoint.ListUsersRequest, PagedResult<UserListItemResponse>>
{
    private readonly IMediator _mediator;

    public ListUsersEndpoint(IMediator mediator) => _mediator = mediator;

    public override void Configure()
    {
        Get("/admin/users");
        Tag(Tags.AdminUsers);
        Summary("List users (Admin)");
    }

    public override async Task HandleAsync(ListUsersRequest req, CancellationToken ct)
    {
        var query = new ListUsersQuery(req.PageNumber, req.PageSize, req.UserType, req.Status, req.SearchTerm);
        Result<PagedResult<UserListItemResponse>> result = await _mediator.QueryAsync(query, ct);

        if (result.IsFailure)
        {
            await EndpointHelper.SendFailureAsync(HttpContext, result, ct);
            return;
        }

        await SendOkAsync(result.Value, ct);
    }

    internal sealed class ListUsersRequest
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? UserType { get; set; }
        public string? Status { get; set; }
        public string? SearchTerm { get; set; }
    }
}
