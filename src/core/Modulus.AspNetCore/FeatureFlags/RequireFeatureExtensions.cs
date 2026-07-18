namespace Modulus.AspNetCore.FeatureFlags;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;

/// <summary>
/// Endpoint-convention gating for the framework's minimal-API (REPR) endpoints —
/// the equivalent of MVC's <c>[FeatureGate]</c>. Attaches an endpoint filter that
/// short-circuits with 404 when a required flag is off, hiding the endpoint's
/// existence rather than advertising a disabled feature.
/// </summary>
public static class RequireFeatureExtensions
{
    /// <summary>
    /// Requires every named feature to be enabled (evaluated per request, so
    /// percentage/time-window/targeting filters see the current context). If any is
    /// off the request returns 404 without invoking the handler. Requires
    /// <see cref="FeatureFlagsExtensions.AddModulusFeatureFlags"/>.
    /// </summary>
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, params string[] features)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var manager = context.HttpContext.RequestServices.GetRequiredService<IFeatureManager>();
            foreach (var feature in features)
            {
                if (!await manager.IsEnabledAsync(feature))
                    return Results.NotFound();
            }

            return await next(context);
        });
        return builder;
    }
}
