using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Modulus.Diagnostics.Endpoints;

using Modulus.AspNetCore.Endpoints;
using Modulus.Core;
using Modulus.Core.Abstractions;
using Modulus.AspNetCore.Security.Policy;
using Modulus.Core.Abstractions.Security;

/// <summary>
/// Maps <c>GET /health/graph</c>: the loaded-module inventory in registration
/// (= initialization) order. Registration order is authoritative in Modulus,
/// so this endpoint reports each module's <c>initOrder</c> rather than a
/// dependency graph.
/// </summary>
internal sealed class ModuleGraphEndpoint : IMinimalEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
        => app.MapGet("/health/graph", Handle)
               .WithTags("Health")
               .Loosen(new LoosenedAttribute("Diagnostics: loaded-module inventory for operators and probes") { Framework = true });

    private static IResult Handle(IModuleLoader loader)
    {
        var modules = loader.GetDescriptors()
            .Select(d => new
            {
                d.Name,
                Type = d.ModuleType.FullName,
                d.InitOrder,
            })
            .ToList();

        return Results.Ok(modules);
    }
}
