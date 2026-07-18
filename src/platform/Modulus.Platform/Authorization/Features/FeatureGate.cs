namespace Modulus.Authorization.Features;

using Modulus.Core.Abstractions;

/// <summary>
/// Bridges <see cref="IFeatureGate"/> to the current request: resolves feature
/// availability for the tenant in scope (<see cref="ICurrentTenant"/>) through the
/// <see cref="IFeatureEntitlementResolver"/>. Scoped. This is the enforcement point the
/// feature layer registers over <see cref="Modulus.Core.Null.NullFeatureGate"/> when
/// <c>AddFeatureGate</c> is called — at which point gating is <b>fail-closed</b>.
/// <para>
/// Tenant handling mirrors tenant isolation exactly: the <b>host</b> context sees every
/// feature (the deliberate cross-tenant/administrative scope, just as it bypasses the
/// tenant query filter), while a request with <b>no tenant resolved</b> — multi-tenancy
/// on but nothing established — sees <b>no</b> gated feature rather than every tenant's
/// set.
/// </para>
/// </summary>
public sealed class FeatureGate(
    ICurrentTenant currentTenant,
    IFeatureEntitlementResolver resolver) : IFeatureGate
{
    public bool IsEnabled(string feature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);

        // Host is the deliberate all-features scope (mirrors the tenant filter's IsHost).
        if (currentTenant.IsHost)
            return true;

        // A resolved tenant defers to its entitlements; no tenant resolved is fail-closed.
        return currentTenant.TenantId is { } tenantId
               && resolver.IsEnabled(tenantId, feature);
    }
}
