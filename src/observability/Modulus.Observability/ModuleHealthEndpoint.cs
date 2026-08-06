using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Modulus.Diagnostics.Endpoints;

using Modulus.AspNetCore.Endpoints;
using Modulus.Core.Abstractions;

internal sealed class ModuleHealthEndpoint : IMinimalEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
        => app.MapGet("/health/modules", HandleAsync)
               .WithTags("Health")
               .AllowAnonymous();

    private static async Task<IResult> HandleAsync(
        IEnumerable<IModuleHealthCheck> checks,
        CancellationToken ct)
    {
        var tasks = checks.Select(c => c.CheckAsync(ct));
        var results = await Task.WhenAll(tasks);

        var isHealthy = results.All(r =>
            r.Status != HealthStatus.Unhealthy);

        return isHealthy
            ? Results.Ok(results)
            : Results.Json(results,
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
