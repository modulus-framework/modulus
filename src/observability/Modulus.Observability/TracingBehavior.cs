namespace Modulus.OpenTelemetry;

using System.Diagnostics;
using Modulus.Core;
using Modulus.Core.Abstractions;
using Modulus.Mediator.Abstractions;
using Modulus.Observability;

public sealed class TracingBehavior<TRequest, TResponse>(
    ICurrentTenant tenant,
    ICurrentUser user)
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var name = typeof(TRequest).Name;

        using var activity = ModulusActivitySources.Mediator
            .StartActivity($"Mediator.{name}");

        if (activity is null) return await next();

        activity.SetTag("modulus.request.type", name);
        activity.SetTag("modulus.request.module",
            typeof(TRequest).Namespace ?? "Unknown");

        if (tenant.IsAvailable)
            activity.SetTag("modulus.tenant.id",
                tenant.TenantId.ToString());

        if (user.UserId.HasValue)
            activity.SetTag("modulus.user.id",
                user.UserId.ToString());

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await next();
            activity.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.SetTag("exception.type", ex.GetType().FullName);
            activity.SetTag("exception.message", ex.Message);
            activity.SetTag("exception.stacktrace", ex.ToString());
            throw;
        }
        finally
        {
            sw.Stop();
            ModulusMeters.MediatorHandlerDuration.Record(sw.Elapsed.TotalMilliseconds);
        }
    }
}
