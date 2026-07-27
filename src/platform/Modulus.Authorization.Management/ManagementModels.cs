namespace Modulus.Authorization.Management;

/// <summary>Creates or replaces grants for a holder.</summary>
/// <param name="HolderType"><c>Role</c> or <c>User</c>.</param>
/// <param name="Holder">Role name, or the user id for user grants.</param>
/// <param name="Permissions">Permission names to grant or deny.</param>
/// <param name="Type"><c>Allow</c> (default when omitted) or <c>Deny</c>.</param>
public sealed record GrantWriteRequest(
    string HolderType, string Holder, string[] Permissions, string? Type);

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
/// <param name="FromRoles">The delegator's roles, snapshotted for capping.</param>
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
