using System.Reflection;

namespace Modulus.Mediator.Behaviors;

using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Abstractions.Attributes;

/// <summary>
/// Enforces <see cref="RequireFeatureAttribute"/>: the feature-entitlement gate of the
/// authorization pipeline (blueprint §5.11, §14). It runs <b>before</b>
/// <see cref="AuthorizationBehavior{TRequest,TResponse}"/> — availability is decided
/// ahead of permission, since a feature disabled for the tenant is inaccessible to
/// everyone regardless of what they may do. Reads <see cref="IFeatureGate"/>, which is
/// <see cref="Modulus.Core.Null.NullFeatureGate"/> (always-on) until
/// <c>AddFeatureGate</c> wires real entitlements, so this behavior is a no-op until then.
/// </summary>
public sealed class FeatureGateBehavior<TRequest, TResponse>(
    IFeatureGate featureGate)
    : IPipelineBehavior<TRequest, TResponse>
{
    // The attribute is fixed per request type; read it once per closed generic.
    private static readonly RequireFeatureAttribute? s_attr =
        typeof(TRequest).GetCustomAttribute<RequireFeatureAttribute>();

    public Task<TResponse> HandleAsync(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var attr = s_attr;

        if (attr is null) return next();

        if (!featureGate.IsEnabled(attr.Feature))
            throw new FeatureDisabledException(attr.Feature);

        return next();
    }
}
