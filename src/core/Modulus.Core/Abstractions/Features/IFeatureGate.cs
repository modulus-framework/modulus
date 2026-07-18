namespace Modulus.Core.Abstractions;

/// <summary>
/// The enforcement seam for <b>feature entitlements</b> — the gate that asks
/// "<i>is this capability available to the current tenant at all?</i>", a dimension
/// <b>above</b> per-user permissions (blueprint §5.11, §14). It sits <i>outside and
/// before</i> the permission check: a feature disabled by entitlement is invisible and
/// inaccessible to <b>everyone</b> in that tenant, including its admins. Distinct from
/// <see cref="ICurrentUser.HasPermission"/>, which asks whether a user may use an
/// <i>available</i> feature.
/// <para>
/// Read this seam from any policy-enforcement point (a mediator behavior, an endpoint
/// filter, a menu builder) exactly as tenant isolation reads <see cref="ICurrentTenant"/>.
/// Implemented by the Authorization module (bridges <see cref="ICurrentTenant"/> + the
/// entitlement resolver); the framework falls back to
/// <see cref="Modulus.Core.Null.NullFeatureGate"/> — everything enabled — when feature
/// management is not configured, so declaring <c>[RequireFeature]</c> without wiring
/// entitlements is a no-op rather than a lock-out (mirrors
/// <see cref="Modulus.Core.Null.NullCurrentTenant"/>).
/// </para>
/// <para>
/// <b>Fail-closed once configured:</b> when feature management <i>is</i> wired, a feature
/// that no plan or tenant override enables is off; and a request with no tenant resolved
/// (a missing header, a background job that forgot to establish one) sees no gated
/// features rather than every tenant's set. The host context is the deliberate
/// all-features scope, never an accident.
/// </para>
/// </summary>
public interface IFeatureGate
{
    /// <summary>
    /// True when <paramref name="feature"/> is available to the current tenant. Deny by
    /// default once feature management is configured: an unknown feature, a feature no
    /// plan grants, or a request with no tenant resolved is <see langword="false"/>.
    /// </summary>
    bool IsEnabled(string feature);
}
