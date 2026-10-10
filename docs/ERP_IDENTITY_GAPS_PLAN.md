# ERP identity gaps: plan and status

Scope: backend and API only (UI skipped for now). Target: a garments ERP (multi-factory group, merchandisers, buyers, suppliers, shop-floor lines).
Verdict from the analysis: authorization is enterprise grade; the gaps were in authentication plumbing, shop-floor and external-party access, and governance data that lived only in code.

Nothing here is committed. Do not commit unless asked.

## Status

| # | Item | Status |
|---|------|--------|
| 1 | Password and lockout policy, history, expiry | Done |
| 2 | Shop-floor sign-in (employee code + PIN, `Identity:ShopFloor`) | Done |
| 3 | SoD rules as data (`/authorization/sod/rules`, `EfSodRuleStore`) | Done |
| 4 | Bulk import without email | **Deferred by the user** ("it will implement later"); build on `IShopFloorAccountService` |
| 6a | Composite roles (`/authorization/roles/inclusions`, `EfRoleInclusionStore`) | Done, tested |
| 6b | Positions (`/authorization/positions`, `EfPositionStore`) | Done, tested (Management 67/67, EF 46/46) |
| 7 | External-party users (`/authorization/parties/*`, `EfPartyStore`) | **Code written, builds clean, NOT tested** (see below) |
| 8 | SCIM 2.0 (Users, Groups) | Not started |
| 9 | Multi-step approval workflow (maybe a `Modulus.Workflow` package) | Not started |
| 10-14 | Tier 3 (passkeys + step-up, session metadata, SAML/LDAP, device-code + back-channel logout, HTTP tests) | Not started |

## Item 7 in progress: where it stands

Design (all in the authorization packages, no Identity change):
- `PartyLinkRow` (`ModulusPartyLinks`): one account links to one party, with a kind (`buyer`, `supplier`, `subcontractor`) and a `PartyId` (Guid).
- Linking writes the user's assignment (type = kind, target = party id), so an existing `AddScopeMap<T>(m => m.AssignedKey("buyer", ...))` and an `assigned:buyer` grant cover exactly that party.
- `PartyCeilingRow` (`ModulusPartyCeilings`): the permissions a kind may ever use (exact or `module:*`). `EfPartyStore.ApplyCeiling` runs at the end of `EfPermissionGrantStore.GetGrants`: a linked account keeps its denies plus grants the ceiling covers; a kind with no rows gets nothing (fail closed). Unlinked accounts are unaffected.
- API under `/authorization/parties`: `GET/PUT links`, `DELETE links/{userId}`, `GET/POST/DELETE ceilings`. Widening a ceiling needs `authorization:grant-any` and refuses `*` and `authorization:*`.

Files: `EfPartyStore.cs`, `AuthorizationStoreDbContext.cs` (rows, sets, filters, tables), `EfPermissionGrantStore.cs`, `EfAuthorizationStoreExtensions.cs`, Management (`MapParties`, models `PartyLinkRequest/Response`, `PartyCeilingRequest`), PublicAPI files updated (run fixapi twice already).

Next steps to finish item 7:
1. Add tests in `tests/unit/Modulus.Authorization.Management.Tests/AuthorizationManagementApiTests.cs`:
   - linked buyer with grants outside its ceiling gets only the ceiling's permissions; denies survive; unlinked user untouched;
   - kind with no ceiling gets nothing;
   - link writes the assignment (`EfAssignmentStore.TargetsFor(user, "buyer", now)` returns the party), relink moves it, unlink removes it;
   - ceiling add needs grant-any, refuses `*` and `authorization:*`, duplicate returns 204, delete of a missing one 404.
2. Run Management and EF tests, `dotnet format --verify-no-changes` on the EF, Management and test projects.
3. Report the limits: delegations to a party account bypass the ceiling; approval limits are not capped by it; in-memory stores ignore parties; needs a migration for `ModulusPartyLinks` and `ModulusPartyCeilings`; creating the party account itself (invitation, role) stays in the app via `IUserInvitationService`.

## Remaining work, in order

### Item 8: SCIM 2.0
`/scim/v2/Users` and `/Groups` over the Identity user and role managers (HR system, Azure AD provisioning). Bearer token of an integration client; per-company; filter, PATCH and pagination per RFC 7644; map groups to roles; audit every change; respect the seat limit (`MaxUsers`).

### Item 9: Multi-step approval workflow
Approval chains with configurable steps that use `ApprovalAuthority` at each step, the SoD trail (`IHasApprovalTrail`), escalation after a timeout and delegation. Probably a separate package so identity stays small. Needs a design pass first.

### Tier 3
10. Passkeys (WebAuthn, `Fido2.AspNet`, MIT) and step-up auth (`acr`/`amr`, a "fresh MFA" requirement for Critical permissions).
11. Session metadata (IP, user agent, device), concurrent-session cap, idle timeout per role.
12. SAML 2.0 SP (BSD-licensed library such as `ITfoxtec.Identity.Saml2`), more than one IdP per app, LDAP/AD bind (`System.DirectoryServices.Protocols`).
13. Back-channel logout and the device-code flow (tablets and scanners without a keyboard).
14. HTTP-level tests for the token endpoint, the authorize flow, federated login and the shop-floor grant.

### Deferred by the user
Item 4, bulk import without email: extend the import engine so employee-code users get an initial PIN or password and "must change at first sign-in"; build on `IShopFloorAccountService`.

## Open gaps from finished items (carry forward)
- Password history is recorded regardless of the policy switch; the cookie login ignores password expiry; no breached-password (HIBP) check.
- Shop-floor: no HTTP-level test of the token handler.
- Role inclusions and positions apply only through the EF grant and approval stores; the in-memory stores, `IUserRoleDirectory` and claims-based role checks do not expand them.
- Adding an inclusion, a holding or an SoD rule does not warn about a resulting violation; SoD is evaluated on the resulting permissions.
- A position's `OrgUnitId` is stored but not enforced.
- Position holdings and inclusions are matched case-insensitively by role name.

## Migrations existing apps will need
`ModulusSodRules`, `ModulusRoleInclusions`, `ModulusPositions`, `ModulusPositionAssignments`, `ModulusPartyLinks`, `ModulusPartyCeilings` (authorization store); `ModulusPasswordHistory`; `EmployeeCode` and `PinHash` columns on the user table.

## Docs still to update when the work stops
- `AGENTS.md` (Security model section): `Identity:ShopFloor`, SoD rules API, role inclusions, positions, parties.
- `docs/SECURITY_STAGE2_PLAN.md`: same, as-built notes.

## Checks per item
`dotnet build modulus.slnx` (0 warnings), `dotnet test modulus.slnx --filter "Category=Unit"`, `dotnet format modulus.slnx --verify-no-changes`; new public API goes in `PublicAPI.Unshipped.txt` (net10 only).
