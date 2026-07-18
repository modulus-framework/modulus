namespace Modulus.Authorization.Features;

/// <summary>
/// A single entry in the module-declared <b>feature catalog</b> — the entitlement
/// counterpart of a <see cref="Modulus.Core.Abstractions.PermissionDefinition"/>
/// (blueprint §5.11, §14). It names a capability whose <i>availability</i> is governed by
/// licensing / subscription tier / jurisdiction, independently of who may use it. The
/// catalog is for declaration and administrative discovery (a plan-builder UI, an
/// entitlement matrix); enforcement is driven by the entitlement store, so an
/// undeclared feature is simply granted by no plan and therefore off.
/// </summary>
/// <param name="Name">The stable feature key (e.g. <c>analytics.advanced</c>), matched by the gate.</param>
/// <param name="DisplayName">A human label for admin surfaces; falls back to <see cref="Name"/> when null.</param>
/// <param name="Description">What the feature covers and why it is gated.</param>
public sealed record FeatureDefinition(string Name, string? DisplayName = null, string? Description = null);

/// <summary>
/// A feature-catalog entry contributed to DI by <c>AddFeatures</c>. Collected into the
/// <see cref="FeatureCatalog"/>.
/// </summary>
/// <param name="Feature">The declared feature.</param>
public sealed record FeatureCatalogRegistration(FeatureDefinition Feature);
