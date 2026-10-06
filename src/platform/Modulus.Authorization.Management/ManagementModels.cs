namespace Modulus.Authorization.Management;

/// <summary>Creates or replaces grants for a holder.</summary>
/// <param name="HolderType"><c>Role</c> or <c>User</c>.</param>
/// <param name="Holder">Role name, or the user id for user grants.</param>
/// <param name="Permissions">Permission names to grant or deny.</param>
/// <param name="Type"><c>Allow</c> (default when omitted) or <c>Deny</c>.</param>
/// <param name="HolderRoles">For user holders: the user's role memberships for the pre-grant SoD simulation. Used only when no
/// <c>IUserRoleDirectory</c> is registered; otherwise the identity store answers and this is ignored.</param>
public sealed record GrantWriteRequest(
    string HolderType, string Holder, string[] Permissions, string? Type, string[]? HolderRoles = null);

/// <summary>A grant as returned by the management API (enum names as strings).</summary>
/// <param name="HolderType"><c>Role</c> or <c>User</c>.</param>
/// <param name="Holder">Role name, or the user id for user grants.</param>
/// <param name="Permission">The permission name.</param>
/// <param name="Type"><c>Allow</c> or <c>Deny</c>.</param>
public sealed record GrantResponse(
    string HolderType, string Holder, string Permission, string Type);

/// <summary>Adds an org unit (with optional parent edges).</summary>
/// <param name="Id">The stable unit id.</param>
/// <param name="Parents">Parent unit ids; empty or omitted for a root unit.</param>
public sealed record OrgUnitWriteRequest(Guid Id, Guid[]? Parents);

/// <summary>Replaces a unit's parents — the reorg primitive.</summary>
/// <param name="Parents">The new parent set; empty makes the unit a root.</param>
public sealed record OrgUnitParentsRequest(Guid[] Parents);

/// <summary>Places a user at an org unit.</summary>
/// <param name="UserId">The user being placed.</param>
/// <param name="OrgUnitId">The unit they are placed at.</param>
/// <param name="Mode"><c>UnitOnly</c>, <c>UnitAndDescendants</c> (default when
/// omitted), or <c>UnitAndAncestors</c>.</param>
public sealed record PlacementWriteRequest(Guid UserId, Guid OrgUnitId, string? Mode);

/// <summary>Defines (or redefines) a plan's feature bundle.</summary>
/// <param name="Features">The features the plan grants.</param>
public sealed record PlanDefinitionRequest(string[] Features);

/// <summary>Assigns a tenant to a plan.</summary>
/// <param name="Plan">The plan name.</param>
public sealed record PlanAssignmentRequest(string Plan);

/// <summary>Sets a per-tenant feature override.</summary>
/// <param name="Enabled">True forces the feature on; false forces it off.</param>
public sealed record OverrideWriteRequest(bool Enabled);

/// <summary>Creates a delegation of authority.</summary>
/// <param name="FromUserId">The delegator whose authority is lent.</param>
/// <param name="FromRoles">Ignored. The server reads the delegator's roles from the identity store; a caller-supplied list
/// would let anyone invent the authority the delegation is capped by. Kept so existing clients still bind.</param>
/// <param name="ToUserId">The delegate.</param>
/// <param name="Permissions">The permissions delegated.</param>
/// <param name="NotBefore">Inclusive window start.</param>
/// <param name="NotAfter">Exclusive window end.</param>
public sealed record DelegationWriteRequest(
    Guid FromUserId,
    string[] FromRoles,
    Guid ToUserId,
    string[] Permissions,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter);

/// <summary>Creates a grant that carries a scope, a validity window or a restriction.</summary>
/// <param name="HolderType"><c>Role</c> or <c>User</c>.</param>
/// <param name="Holder">Role name, or the user id for user grants.</param>
/// <param name="Permission">One registered permission (no wildcards).</param>
/// <param name="Type"><c>Allow</c> (default), <c>Restrict</c> (narrows what the holder's other grants cover) or <c>Deny</c> (a temporary block when it has a window).</param>
/// <param name="Scope"><c>tenant</c> (default), <c>own</c>, <c>org</c>, <c>org:{unitId}</c> or <c>assigned:{type}</c>. A restriction needs a narrower scope; a deny takes none.</param>
/// <param name="ValidFrom">The grant applies from this instant.</param>
/// <param name="ValidUntil">The grant stops applying at this instant, with no further action. Required to be accompanied by a reason.</param>
/// <param name="Reason">Why the grant was made; required for a temporary grant.</param>
public sealed record ScopedGrantWriteRequest(
    string HolderType, string Holder, string Permission, string? Type, string? Scope,
    DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil, string? Reason);

/// <summary>A scoped grant as returned by the management API.</summary>
public sealed record ScopedGrantResponse(
    Guid Id, string HolderType, string Holder, string Permission, string Type, string Scope,
    DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil, string? Reason, Guid? CreatedBy, DateTimeOffset CreatedAt);

/// <summary>Links a user to a business object, with an optional effective window.</summary>
/// <param name="UserId">The assigned user.</param>
/// <param name="AssignmentType">The kind of object (<c>customer</c>, <c>warehouse</c>, ...), as named by the module that scopes on it.</param>
/// <param name="TargetId">The object.</param>
/// <param name="ValidFrom">Effective from this instant.</param>
/// <param name="ValidUntil">Effective until this instant.</param>
public sealed record AssignmentWriteRequest(
    Guid UserId, string AssignmentType, Guid TargetId, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil);
