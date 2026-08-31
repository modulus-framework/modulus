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
        // Per-check isolation: a check that throws (or hangs) reports
        // Unhealthy instead of failing the whole endpoint with a 500 — the
        // other modules' statuses must still be observable.
        var results = await Task.WhenAll(checks.Select(async c =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                // Bounded per check so one hung dependency cannot stall the
                // health probe past the orchestrator's own timeout.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                return await c.CheckAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                stopwatch.Stop();
                return new ModuleHealthResult(
                    c.GetType().Name, HealthStatus.Unhealthy,
                    "Health check timed out after 5s.",
                    stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return new ModuleHealthResult(
                    c.GetType().Name, HealthStatus.Unhealthy,
                    $"Health check threw: {ex.Message}",
                    stopwatch.Elapsed);
            }
        }));

        var isHealthy = results.All(r =>
            r.Status != HealthStatus.Unhealthy);

        return isHealthy
            ? Results.Ok(results)
            : Results.Json(results,
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
