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
        // Per-check isolation via the shared runner: a check that throws (or
        // hangs) reports Unhealthy instead of failing the whole endpoint with
        // a 500 — the other modules' statuses must still be observable.
        var results = await Task.WhenAll(
            checks.Select(c => ModuleHealthCheckRunner.RunIsolatedAsync(c, ct)));

        var isHealthy = results.All(r =>
            r.Status != HealthStatus.Unhealthy);

        return isHealthy
            ? Results.Ok(results)
            : Results.Json(results,
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
