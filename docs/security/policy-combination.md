# Authorization policy combination

How Modulus combines the layers of an authorization decision, and what wins when they disagree. Every rule here is
deterministic and covered by a test (named in brackets). When a layer cannot be evaluated, the answer is **deny**.

## The layers, in the order they are applied

| # | Layer | Question | Where |
|---|-------|----------|-------|
| 1 | Identity | Is there a verified principal? | authentication scheme, `ICurrentUser` |
| 2 | Tenant (company) | Is the caller a member of the selected company? Is it active? | `TenantMiddleware`, `RequireMembership` |
| 3 | Endpoint policy | Does the endpoint declare how it is protected? | security guard, `Loosen(reason)` |
| 4 | Permission | Does the caller hold the permission? | `PermissionResolver` |
| 5 | Scope | Over which data does the permission apply? | `PermissionScopeResolver`, `IScopeEnforcer` |
| 6 | Resource rules | Is this record, in this state, allowed for this action? | `ResourcePolicy` |
| 7 | Fields | Which fields may the caller see or set? | `IFieldAuthorizer` |

A request is allowed only if **every applicable layer allows it**. A broad grant at one layer never bypasses a narrower
requirement at another: a tenant-wide `inventory:stock:view` does not open a record the resource rules refuse, and a
permission does not cross a company boundary.

## Rules inside the permission layer

1. **Deny wins.** An explicit `Deny` (role or user, wildcard included) removes the permission, whatever allows exist, and
   whatever scope they carry. [`RoleDeny_OverridesRoleAllow`, `UserDeny_OverridesRoleAllow`, `An_explicit_deny_wins_over_every_scope`]
2. **Deny applies after implication.** An allow confers what the permission `Requires`; a deny on the implied
   permission still removes it. [`Deny_IsAppliedAfterImplicationClosure`]
3. **Allows union, restrictions intersect.** Several allows add up (`Own` ∪ `Assigned` ∪ ...). A `Restrict` grant narrows
   every alternative and never makes a permission effective by itself. [`Scopes_from_several_grants_union`,
   `A_restriction_narrows_a_wider_grant`, `A_restriction_alone_grants_nothing`]
4. **Wildcards stop short of Critical.** `module:group:*` as an allow never confers a `Critical` permission; as a deny it
   removes them. [`PermissionSensitivityTests`]
5. **Time is decided at the decision.** A grant outside `ValidFrom`/`ValidUntil` does not apply; no cleanup job is needed.
   [`A_grant_stops_applying_when_it_expires_with_no_administrator_action`]
6. **Unknown or unregistered is nothing.** An unknown permission, an unknown wildcard prefix and an anonymous caller resolve to
   no access. [`NoGrants_ResolvesToEmpty_FailClosed`, `UnknownWildcardPrefix_ExpandsToNothing_FailClosed`]
7. **Delegation lends, it does not add.** A delegate receives the delegator's own scope for the delegated permissions, capped at
   what the delegator holds, never re-delegated, and a deny on the delegate wins over it. [`A_delegate_gets_the_delegators_scope_never_more`,
   `A_deny_on_the_delegate_wins_over_a_delegation`]

## Rules inside the resource layer

1. Rules are evaluated **deny-by-default with deny-override**: a satisfied `Deny` rule refuses the action; otherwise some
   satisfied `Allow` rule must match; no match refuses. [`ResourcePolicy`]
2. A rule that throws counts as not matched and refuses with `EVALUATION_ERROR`; it never propagates as permission.
3. A type with no policy gets `METADATA_MISSING` (deny).
4. Separation of duties (`SOD_CONFLICT`) is a resource-layer denial and is checked when the action runs, not when a screen renders.

## Mandatory versus ordinary

* **Mandatory (cannot be overridden by a role or grant):** a missing or unverified identity, a suspended or unknown company,
  a revoked membership, the cross-tenant write guard, a failed endpoint policy, an explicit deny, and the Critical-permission
  wildcard rule.
* **Ordinary (granted and revoked through roles, grants, scopes, delegations):** everything else.
* A mandatory denial is never turned into a permit by a more specific ordinary grant. Break-glass access to a company
  (`HostTenantAccessPolicy`) is itself an audited, separately authorized decision, not a role.

## Lists, single records and exports must agree

The same scope that filters a list (`IScopeEnforcer.Apply`) is the one that checks a single record. A new route that returns
the same data (by id, export, report, bulk action) must go through that scope too. [`Lists_and_single_record_checks_agree_for_every_scope_shape`]

## Revocation windows

Decisions are computed per request from the grant store; nothing about grants is cached across requests
(`CachedPermissionGrantStore` memoizes within one request only). So a removed grant, deny, scope, assignment, membership or
approval limit applies on the **next request**, on every node. The places where state is held longer, and for how long:

| State | Held for | How to tighten |
|-------|----------|----------------|
| Grants, scopes, assignments, memberships, approval limits | one request | n/a |
| Delegation snapshot of the delegator's roles | `DelegationRoleRefreshOptions.Interval` (1 min), plus an immediate refresh through `IAccessChangeObserver` on the node that made the change | lower the interval |
| Org hierarchy snapshot (`EfOrgHierarchy`) | 30 s per node; the node that edits it drops its own snapshot at once, other nodes wait for expiry | lower `CacheDuration` |
| `permission` claims inside a token | until the token expires | `TrustPermissionClaims = false` makes the grant store the only source |
| Access token of a disabled or deleted account | until it expires, unless `Identity:ValidateTokenEntries` (default `true`) checks the stored token entry, which revocation removes | keep it on |
| The AI platform's cached access scope | up to 5 min, or until the connector's revocation signal is delivered (`IAccessChangeObserver`; durable with `RevocationSpoolFile`) | register observers for new code that changes access |

Rule for new code: anything that changes grants, roles, memberships, accounts or limits calls
`NotifyAccessChangedAsync`. A mandatory dependency that cannot be read fails the request; it never falls back to a stale grant.

## Explaining a decision

`ResourcePolicy.Explain`, `ResourceAuthorizer.Explain` and the `/authorization/resources/{type}/{id}/explain` endpoint list
which rules matched, for administrators; `AccessReasonCodes` names the outcome in audit events and API errors.
