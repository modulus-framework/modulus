# Privileged access: classification, break-glass and exceptions

Operator procedure for the privileged permissions (owner decision D10, 2026-10-23). It describes what the framework does
today. Gaps are listed as gaps, not as controls.

## 1. Privileged permissions (Critical, as built)

A `Critical` permission needs a fresh multi-factor sign-in in production (`StepUp.RequireForCritical`), and a wildcard grant
never confers one. Generated production apps also require a passkey sign-in (`hwk`/`swk`) for the permissions named in
`StepUpOptions.PhishingResistantPermissions`.

| Permission | Covers | Registered by |
|---|---|---|
| `identity:accounts:manage` | create, change, disable and delete accounts | `AccountLifecycleExtensions` |
| `scim:manage` | SCIM provisioning | `ScimExtensions` |
| `tenancy:data-stores:manage` | tenant database registrations | `TenantDataStoreEndpoints` |
| `authorization:manage` | grants, org structure, delegations | `AuthorizationManagementExtensions` |
| `authorization:grant-any` | grant permissions the administrator does not hold | `AuthorizationManagementExtensions` |
| `authorization:break-glass` | activate emergency access | `AuthorizationManagementExtensions` |
| `authorization:entitlements:manage` | platform feature plans and company overrides | `AuthorizationManagementExtensions` |
| `settings:global:manage` | installation-wide setting values | `SettingsAdministrationEndpoints` |
| `messaging:manage` | outbox inspection and dead-letter replay | `OutboxManagementExtensions` (Critical since 2026-10-23) |

Not yet classified, because the framework has no surface for them:

- **Audit configuration, retention changes, legal holds and export.** Retention is configured in code (`AddModulusSecurityAuditRetention`); no admin endpoint exists. Nothing to enforce until one does.
- **Secret and cryptographic-key access.** Key material is managed by the operator (OpenBao, Data Protection key ring). No framework endpoint returns it.
- **MFA and recovery controls.** Enrolment is self-service on the signed-in person's own account, not an administrator surface.
- **Database administration, deployment and disabling monitoring.** Operator work outside the application.
- **Framework settings that affect tenant isolation.** The installation-wide settings are covered by `settings:global:manage`; no separate permission exists.

When an admin endpoint for one of these gaps is added, it must map to a Critical permission. The startup guard refuses an endpoint
with no policy at all; choosing Critical is a review step, not something the framework enforces.

## 2. Break-glass

Break-glass gives emergency access without a second approver. It is off until the application names profiles.

1. **Configure a profile** in `AuthorizationManagementOptions.BreakGlassProfiles`: the permissions it grants (no wildcard) and a
   `MaxHours` (default 4). An empty dictionary means break-glass is off.
2. **Activate.** A holder of `authorization:break-glass` calls `POST /authorization/access-requests/break-glass` with the profile
   name and a reason of at least 10 characters. The grants take effect at once and end at `MaxHours`. The activation is recorded
   as `break-glass.activated` in the security audit.
3. **Review.** The activation is listed by `GET /authorization/access-requests/break-glass/unreviewed` until someone reviews it with
   `POST /authorization/access-requests/{id}/review`. The reviewer must be someone other than the person who used the access; the
   framework refuses self-review.
4. **Close the loop.** Set a review deadline for each activation (the operator's choice; the framework enforces none). An activation still unreviewed past that deadline is an open finding.

Not built: notifications to approvers and per-profile approver lists. Until they exist, the operator watches the unreviewed list.

## 3. Exception register

An exception records why a holder keeps a Critical permission: the justification, the business owner who approved it, a
compensating control, the end date and a review. The register is stored in the authorization database (table
`ModulusPermissionExceptions`, tenant-scoped, upgrade script step 4) and served under `/authorization/exceptions` by
`MapModulusPermissionExceptions()`. A host maps the routes itself; the generated applications do not map them yet.

Rules, as built:

- Only a Critical permission can have an exception. The holder is a role or another user; a person cannot record one for themselves.
- The justification is at least 20 characters. The end date is in the future and at most 90 days away (`PermissionExceptionOptions`).
- The review date is after now and no later than the end date. The person who recorded an exception cannot review it.
- An exception is reviewed once and can be ended early once. Both are audited (`PermissionException`).

The register reports and does not enforce. `GET /authorization/exceptions/findings` lists each role or user grant of a Critical
permission that no active exception explains, and marks those whose exception has ended. Run it on the review cadence and treat each
finding as an open item. Revoking the grant is what removes access; ending the exception only changes what the register reports.

Service identities use the `integration:` role and need their own grant; they never inherit a person's privileged access.

## 4. Verification

- Build and test: `OutboxManagementApiTests.Replaying_and_inspecting_the_outbox_is_a_critical_permission`, the step-up tests
  (`StepUpTests`) and `PermissionExceptionApiTests` (8).
- Production: a privileged route with a password-only token must return 401 `STEP_UP_REQUIRED`; with a passkey token, 200 (the
  check recorded in §14.6 of `docs/IDENTITY_PLATFORM_PLAN.md`, run 2026-10-23 on a generated app with scratch-only setup).
