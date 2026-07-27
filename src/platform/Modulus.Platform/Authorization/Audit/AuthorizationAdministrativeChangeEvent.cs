namespace Modulus.Authorization.Audit;

using Modulus.Events.Abstractions;

/// <summary>
/// A durable record of an administrative authorization change — grant/revoke,
/// role or org-structure edit, feature-entitlement change, delegation
/// create/revoke (auth blueprint §5.14/§16: "who was granted what, by whom,
/// when"). Emitted unconditionally for every mutating call through
/// <c>Modulus.Authorization.Management</c>'s admin API, regardless of whether
/// any decision-auditing is configured — administrative changes are always
/// audited, unlike the declaratively-scoped decision auditing the blueprint
/// separately describes for allow/deny access decisions.
/// </summary>
/// <param name="Category">
/// The kind of thing changed: <c>"Grant"</c>, <c>"OrgUnit"</c>,
/// <c>"OrgPlacement"</c>, <c>"FeatureEntitlement"</c>, or <c>"Delegation"</c>.
/// </param>
/// <param name="Action">
/// What happened to it, e.g. <c>"Granted"</c>, <c>"Revoked"</c>,
/// <c>"Created"</c>, <c>"Reparented"</c>, <c>"Placed"</c>, <c>"Removed"</c>,
/// <c>"PlanDefined"</c>, <c>"PlanAssigned"</c>, <c>"OverrideSet"</c>,
/// <c>"OverrideCleared"</c>.
/// </param>
/// <param name="ActorUserId">
/// Who made the change (from <c>ICurrentUser.UserId</c>), or <see langword="null"/>
/// if the caller could not be identified (should not normally happen — every
/// management endpoint requires the <c>authorization:manage</c> permission).
/// </param>
/// <param name="TargetDescription">
/// A short, human-readable description of what was affected, e.g.
/// <c>"role:Admin"</c> or <c>"user:3fa8...→delegate:9c1b..."</c>.
/// </param>
/// <param name="Details">
/// Structured key/value detail specific to the category (e.g. the permission
/// names granted, the org-unit id, the plan name) — the "deciding factor"
/// context an auditor needs beyond category/action/target.
/// </param>
[IntegrationEventName("authorization.administrative-change.v1")]
public sealed record AuthorizationAdministrativeChangeEvent(
    string Category,
    string Action,
    string? ActorUserId,
    string TargetDescription,
    IReadOnlyDictionary<string, string> Details)
    : IntegrationEventBase("authorization.administrative-change.v1");
