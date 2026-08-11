using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Modulus.Mediator.Abstractions;
using ModulusSample.Modules.Partners.Application.Commands;
using ModulusSample.Modules.Partners.Application.Queries;

namespace ModulusSample.Modules.Partners.Presentation;

public static class PartnersEndpoints
{
    public static void MapPartnersEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/partners")
            .WithName("Partners")
            .WithOpenApi();

        group.MapPost("/", CreatePartner)
            .WithName("CreatePartner")
            .WithOpenApi();

        group.MapGet("/{id}", GetPartnerById)
            .WithName("GetPartnerById")
            .WithOpenApi();

        group.MapGet("/", ListPartners)
            .WithName("ListPartners")
            .WithOpenApi();
    }

    private static async Task<IResult> CreatePartner(
        HttpContext context,
        IMediator mediator,
        CreatePartnerRequest request)
    {
        var command = new CreatePartnerCommand(
            request.Name,
            request.Type,
            request.Email,
            request.Phone,
            request.Address);

        var result = await mediator.SendAsync(command);

        return result.IsSuccess
            ? Results.Created($"/api/partners/{result.Value}", new { id = result.Value })
            : Results.BadRequest(new { error = result.Error?.Message });
    }

    private static async Task<IResult> GetPartnerById(
        IMediator mediator,
        Guid id)
    {
        var query = new GetPartnerByIdQuery(id);
        var result = await mediator.QueryAsync(query);

        return result is not null
            ? Results.Ok(result)
            : Results.NotFound();
    }

    private static async Task<IResult> ListPartners(
        IMediator mediator,
        int page = 1,
        int pageSize = 10)
    {
        var query = new ListPartnersQuery(page, pageSize);
        var result = await mediator.QueryAsync(query);

        return Results.Ok(result);
    }
}

public sealed record CreatePartnerRequest(
    string Name,
    string Type,
    string Email,
    string Phone,
    string Address);
