# 0013. Authentik as sole identity provider (PKCE-only) with roles and permissions

Date: 2026-09-16

Status: accepted

## Context

The sample previously had no authentication: a hand-rolled MVP
`POST /api/auth/authenticate` minted tokens with zero credential checks, and
all 17 business endpoints were effectively open. The goal is a realistic
modular-monolith security story: a single external IdP, group-based roles,
and fine-grained permissions enforced on every endpoint.

## Decision

- **Authentik is the sole IdP** (local Docker stack on `http://localhost:9010`,
  seeded via REST: `meetup` OAuth2 provider + application, three groups,
  three dev users). The provider is a **public client**: `grant_types =
  authorization_code + refresh_token`, no secret anywhere — the API fails fast
  at startup if `Identity:ExternalProviders:Authentik:ClientSecret` is set.
- **Roles are Authentik groups** (`meetup-members`, `meetup-organizers`,
  `meetup-admins`, see `MeetupRoles`). JwtBearer maps the `groups` claim to
  both `IsInRole` (`RoleClaimType = groups`) and `ClaimTypes.Role` (mirrored
  in `OnTokenValidated`, materialized with `ToList()` before mutating the
  identity — lazy enumeration over a mutating claim list throws).
- **Permissions are colon-style** (`registrations:register`, …) so
  `ModulusPermissionPolicyProvider` picks them up; every endpoint demands
  role AND permission (AND semantics), except member self-service endpoints
  which are permission-only.
- **Per-application issuer**: JwtBearer validates against
  `{Authority}/application/o/{ClientId}/` (Authentik mints per-app issuers);
  the framework `AddAuthentik` OIDC handler stays interactive-only with the
  same loopback-HTTP allowance.
- **Commands carry `[SkipTransaction]`**: every handler commits exactly once
  through its own module `IUnitOfWork`, so EF's implicit `SaveChanges`
  transaction plus the transactional outbox give full atomicity.
  `[Transactional(typeof(XDbContext))]` would couple Application to the
  Infrastructure `DbContext` type and is reserved for future multi-context
  handlers.

## Consequences

- Verified end-to-end with real PKCE tokens: member 403s on admin routes,
  organizer proposes, admin confirms, cross-module events flow
  (registration → user, proposal acceptance → meeting group).
- Local boot now requires the Authentik stack (`docker compose up -d` +
  seeded app, see README §5); anonymous calls get 401.
- Password grant is NOT enabled on the provider — smoke tests mint tokens
  through the real authorization-code + PKCE flow (see
  `Meetup.postman_collection.json`, Auth folder).
- Framework fix bundled: `AddAuthentik`/`AddAuth0`/`AddOkta`/`AddAzureAd`/
  `AddDuendeIdentityServer`/`AddKeycloak` now register their bound options
  snapshot as a singleton — previously the provider constructor parameter
  (`AuthentikOptions`, …) was unresolvable and crashed container validation
  in Development (covered by
  `ExternalProviderOptionsRegistrationTests`, 6 tests).
