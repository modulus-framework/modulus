namespace Modulus.AspNetCore.FeatureFlags;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Modulus.Core.Abstractions;

/// <summary>
/// Endpoint-convention gating for the framework's minimal-API (REPR) endpoints —
/// the equivalent of MVC's <c>[FeatureGate]</c>. Attaches an endpoint filter that
/// short-circuits with 404 when a required feature is unavailable, hiding the
/// endpoint's existence rather than advertising a disabled feature.
/// </summary>
public static class RequireFeatureExtensions
{
    /// <summary>
    /// Requires every named feature to pass <b>both</b> feature layers, evaluated
    /// per request:
    /// <list type="bullet">
    /// <item><see cref="IFeatureGate"/> — <b>commercial availability</b>: is the
    /// feature entitled to the current tenant (plan + overrides)? Fail-closed once
    /// <c>AddFeatureGate</c> is configured; a no-op until then.</item>
    /// <item><see cref="IFeatureManager"/> — <b>operational rollout</b>: is the
    /// flag currently on (percentage/time-window/targeting filters see the current
    /// context)? Consulted only when
    /// <see cref="FeatureFlagsExtensions.AddModulusFeatureFlags"/> is configured.</item>
    /// </list>
    /// A feature is served only when the tenant is entitled to it <i>and</i> the
    /// rollout flag (if any) is on. If any named feature fails either layer the
    /// request returns 404 without invoking the handler.
    /// </summary>
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, params string[] features)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
            await IsAllowedAsync(context.HttpContext.RequestServices, features)
                ? await next(context)
                : Results.NotFound());
        return builder;
    }

    /// <summary>
    /// The composed decision: entitlement gate AND rollout flag, each consulted
    /// only when its system is registered. Internal for tests.
    /// </summary>
    internal static async ValueTask<bool> IsAllowedAsync(
        IServiceProvider services, string[] features)
    {
        // NullFeatureGate (allow-all) until AddFeatureGate wires entitlements; a
        // container without the mediator/authorization defaults has no gate at all.
        var gate = services.GetService<IFeatureGate>();
        var manager = services.GetService<IFeatureManager>();

        foreach (var feature in features)
        {
            if (gate is not null && !gate.IsEnabled(feature))
                return false;
            if (manager is not null && !await manager.IsEnabledAsync(feature))
                return false;
        }

        return true;
    }
}
