using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Modulus.Diagnostics.Endpoints;

using Modulus.AspNetCore.Endpoints;
using Modulus.Core;
using Modulus.Core.Abstractions;

internal sealed class ModuleGraphEndpoint : IMinimalEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
        => app.MapGet("/health/graph", Handle)
               .WithTags("Health")
               .AllowAnonymous();

    private static IResult Handle(IModuleLoader loader)
    {
        var descriptors = loader.GetDescriptors();

        var graph = descriptors.Select(d => new
        {
            d.Name,
            d.InitOrder,
            DependsOn = d.Dependencies
                .Select(t => t.Name).ToList(),
        });

        return Results.Ok(graph);
    }
}