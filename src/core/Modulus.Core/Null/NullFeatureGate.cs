namespace Modulus.Core.Null;

using Modulus.Core.Abstractions;

/// <summary>
/// The feature gate used when feature management is not configured. Reports every
/// feature as enabled, so a <c>[RequireFeature]</c> guard or a
/// <see cref="IFeatureGate"/> check is a no-op and every capability stays available —
/// the same semantics <see cref="NullCurrentTenant"/> gives tenant isolation when
/// multi-tenancy is off. Entitlement restriction begins only once a real
/// <see cref="IFeatureGate"/> is registered (via the Authorization module's
/// <c>AddFeatureGate</c>), at which point it becomes fail-closed.
/// </summary>
public sealed class NullFeatureGate : IFeatureGate
{
    /// <summary>Shared stateless instance.</summary>
    public static readonly NullFeatureGate Instance = new();

    /// <summary>
    /// Feature management is not configured, so there is nothing to gate: every feature
    /// is available.
    /// </summary>
    public bool IsEnabled(string feature) => true;
}
