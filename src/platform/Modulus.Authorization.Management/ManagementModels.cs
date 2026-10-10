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

/// <summary>An approval limit to add ("this holder may use this permission on documents up to this amount").</summary>
/// <param name="HolderType"><c>Role</c> or <c>User</c>.</param>
/// <param name="Holder">Role name, or the user id for a user limit.</param>
/// <param name="Permission">One registered permission (no wildcards).</param>
/// <param name="MaxAmount">The largest document value the holder may act on (zero or more).</param>
/// <param name="Currency">The currency of the limit; omit for documents without a currency.</param>
/// <param name="DocumentType">The document type (the resource's type name); omit for every type.</param>
/// <param name="OrgUnitId">Limits the authority to this org unit and its descendants; omit for any.</param>
/// <param name="ValidFrom">Effective from this instant.</param>
/// <param name="ValidUntil">Effective until this instant.</param>
public sealed record ApprovalAuthorityWriteRequest(
    string HolderType, string Holder, string Permission, decimal MaxAmount, string? Currency, string? DocumentType,
    Guid? OrgUnitId, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil);

/// <summary>A stored approval limit.</summary>
public sealed record ApprovalAuthorityResponse(
    Guid Id, string HolderType, string Holder, string Permission, decimal MaxAmount, string? Currency, string? DocumentType,
    Guid? OrgUnitId, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil, Guid? CreatedBy, DateTimeOffset CreatedAt);

/// <summary>The business details of an org unit.</summary>
/// <param name="Code">A short code, unique within the company.</param>
/// <param name="Name">The display name.</param>
/// <param name="Kind">What it is: branch, factory, office, warehouse, department, team, or another word.</param>
/// <param name="ManagerUserId">The user who manages it.</param>
public sealed record OrgUnitProfileRequest(string Code, string Name, string Kind, Guid? ManagerUserId);

/// <summary>An org unit's business details.</summary>
public sealed record OrgUnitProfileResponse(
    Guid UnitId, string Code, string Name, string Kind, bool IsClosed, Guid? ManagerUserId, DateTimeOffset? ClosedAt);

/// <summary>The legal and regional details of the company.</summary>
public sealed record CompanyProfileRequest(
    string LegalName, string? TradeName, string? RegistrationNumber, string? TaxId, string? Address, string? Country,
    string? Currency, int? FiscalYearStartMonth, string? TimeZone, string? Language);

/// <summary>Asks for temporary access.</summary>
/// <param name="Permissions">The registered permissions needed (no wildcards).</param>
/// <param name="Reason">Why; the approver reads it.</param>
/// <param name="Hours">How long the access should last once approved.</param>
public sealed record AccessRequestWriteRequest(string[]? Permissions, string? Reason, int Hours);

/// <summary>An approver's or reviewer's note.</summary>
public sealed record AccessRequestDecisionRequest(string? Note);

/// <summary>Activates emergency access.</summary>
/// <param name="Profile">The break-glass profile name.</param>
/// <param name="Reason">Why (at least 10 characters); reviewed afterwards.</param>
/// <param name="Hours">How long; defaults to the profile's maximum.</param>
public sealed record BreakGlassWriteRequest(string? Profile, string? Reason, int? Hours);

/// <summary>An access request or break-glass use.</summary>
public sealed record AccessRequestResponse(
    Guid Id, string Kind, Guid RequesterId, IReadOnlyList<string> Permissions, string Reason, int Hours, string Status, DateTimeOffset CreatedAt,
    Guid? DecidedBy, DateTimeOffset? DecidedAt, string? Note, DateTimeOffset? AccessEndsAt, Guid? ReviewedBy, DateTimeOffset? ReviewedAt);

/// <summary>A segregation-of-duties rule to store.</summary>
/// <param name="Name">A stable name for the control. A name used by a rule declared in code replaces that rule.</param>
/// <param name="Permissions">Two or more registered permissions (no wildcards) of which one person may hold at most one.</param>
/// <param name="Rationale">Why the separation exists, for auditors.</param>
/// <param name="IsEnabled">False switches the rule off (and hides a rule of the same name declared in code). Defaults to true.</param>
public sealed record SodRuleWriteRequest(string Name, IReadOnlyCollection<string> Permissions, string? Rationale, bool? IsEnabled);

/// <summary>A segregation-of-duties rule in force.</summary>
/// <param name="Id">The stored rule's id; null for a rule declared in code.</param>
/// <param name="Source"><c>stored</c> or <c>code</c>.</param>
/// <param name="Name">The rule name.</param>
/// <param name="Permissions">The mutually exclusive permissions.</param>
/// <param name="Rationale">Why the separation exists.</param>
/// <param name="IsEnabled">False while the rule is switched off.</param>
/// <param name="CreatedBy">The administrator who made it, or null.</param>
/// <param name="CreatedAt">When it was made, or null for a rule declared in code.</param>
/// <param name="UpdatedAt">When it was last changed, or null.</param>
public sealed record SodRuleResponse(
    Guid? Id, string Source, string Name, IReadOnlyCollection<string> Permissions, string? Rationale, bool IsEnabled,
    Guid? CreatedBy, DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt);

/// <summary>Makes a role include another: holders of <see cref="Role"/> also hold everything <see cref="Includes"/> holds.</summary>
/// <param name="Role">The including role.</param>
/// <param name="Includes">The included role.</param>
public sealed record RoleInclusionRequest(string Role, string Includes);

/// <summary>A stored role inclusion.</summary>
/// <param name="Role">The including role.</param>
/// <param name="Includes">The included role.</param>
/// <param name="CreatedBy">The administrator who made it, or null.</param>
/// <param name="CreatedAt">When it was made.</param>
public sealed record RoleInclusionResponse(string Role, string Includes, Guid? CreatedBy, DateTimeOffset CreatedAt);

/// <summary>Links an account to an external party.</summary>
/// <param name="UserId">The account.</param>
/// <param name="Kind">The kind, such as buyer, supplier or subcontractor.</param>
/// <param name="PartyId">The party record the account belongs to.</param>
public sealed record PartyLinkRequest(Guid UserId, string Kind, Guid PartyId);

/// <summary>An account's link to a party.</summary>
/// <param name="UserId">The account.</param>
/// <param name="Kind">The kind.</param>
/// <param name="PartyId">The party.</param>
/// <param name="CreatedBy">The administrator who made it, or null.</param>
/// <param name="CreatedAt">When it was made.</param>
public sealed record PartyLinkResponse(Guid UserId, string Kind, Guid PartyId, Guid? CreatedBy, DateTimeOffset CreatedAt);

/// <summary>Lets a party kind use a permission.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Permission">An exact permission or a prefix ending in a star, such as orders:*.</param>
public sealed record PartyCeilingRequest(string Kind, string Permission);

/// <summary>Creates a position.</summary>
/// <param name="Code">The unique code within the company, such as "SEW-L3-SUP".</param>
/// <param name="Name">The display name.</param>
/// <param name="OrgUnitId">The org unit it belongs to, or null.</param>
/// <param name="Roles">The roles whoever holds it gets.</param>
public sealed record PositionCreateRequest(string Code, string Name, Guid? OrgUnitId, IReadOnlyCollection<string> Roles);

/// <summary>Replaces a position's details.</summary>
/// <param name="Name">The display name.</param>
/// <param name="OrgUnitId">The org unit it belongs to, or null.</param>
/// <param name="Roles">The roles whoever holds it gets.</param>
/// <param name="IsActive">False while the position grants nothing.</param>
public sealed record PositionUpdateRequest(string Name, Guid? OrgUnitId, IReadOnlyCollection<string> Roles, bool IsActive);

/// <summary>A position.</summary>
/// <param name="Id">The position id.</param>
/// <param name="Code">The unique code.</param>
/// <param name="Name">The display name.</param>
/// <param name="OrgUnitId">The org unit, or null.</param>
/// <param name="Roles">The roles it grants.</param>
/// <param name="IsActive">False while it grants nothing.</param>
public sealed record PositionResponse(Guid Id, string Code, string Name, Guid? OrgUnitId, IReadOnlyCollection<string> Roles, bool IsActive);

/// <summary>Puts a user in a position.</summary>
/// <param name="UserId">The holder.</param>
/// <param name="ValidFrom">When the holding starts; defaults to now.</param>
/// <param name="ValidUntil">When it ends, or null for open-ended.</param>
public sealed record PositionHoldRequest(Guid UserId, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil);

/// <summary>A user's holding of a position.</summary>
/// <param name="Id">The holding id.</param>
/// <param name="PositionId">The position.</param>
/// <param name="UserId">The holder.</param>
/// <param name="ValidFrom">When it starts.</param>
/// <param name="ValidUntil">When it ends, or null.</param>
/// <param name="CreatedBy">The administrator who made it, or null.</param>
/// <param name="CreatedAt">When it was made.</param>
public sealed record PositionHoldResponse(Guid Id, Guid PositionId, Guid UserId, DateTimeOffset ValidFrom, DateTimeOffset? ValidUntil, Guid? CreatedBy, DateTimeOffset CreatedAt);
