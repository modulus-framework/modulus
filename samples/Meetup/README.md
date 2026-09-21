# Meetup — Modular Monolith with DDD

Full Modular Monolith .NET application with a Domain-Driven Design approach,
built on the [Modulus framework](../../README.md). The domain is modelled on
[kgrzybek/modular-monolith-with-ddd](https://github.com/kgrzybek/modular-monolith-with-ddd)
(Meeting Groups a la Meetup.com) and re-implemented here with Modulus building
blocks: explicit module registration, per-module `DbContext`, CQRS via
`Modulus.Mediator`, and integration events via `Modulus.Events`.

## Table of contents

[1. Introduction](#1-introduction)

[2. Domain](#2-domain)

[3. Architecture](#3-architecture)

[4. Technology](#4-technology)

[5. How to Run](#5-how-to-run)

[6. API Reference](#6-api-reference)

[7. Contribution](#7-contribution)

[8. Roadmap](#8-roadmap)

[9. Authors](#9-authors)

[10. License](#10-license)

[11. Inspirations and Recommendations](#11-inspirations-and-recommendations)

## 1. Introduction

### 1.1 Purpose of this Repository

- Showing how you can implement a **monolith** application in a **modular** way
  on top of the Modulus framework
- Presentation of the **full implementation** of an application — not a PoC:
  five business modules, authentication, validation, correlation, idempotency,
  security headers, health probes, OpenAPI
- Showing the application of **best practices** and OOP principles
  (one type per file, rich domain model, encapsulation)
- Presentation of **Domain-Driven Design** tactical patterns
  (aggregates, entities, domain invariants, integration events)
- Presentation of **CQRS** with a mediator pipeline
  (commands/queries, FluentValidation, unit of work)
- Presentation of **event-driven integration** between modules
- Presentation of the **C4 model** and **diagrams-as-text**
  (see [docs/C4](docs/C4))
- Presentation of an **Architecture Decision Log**
  (see [docs/architecture-decision-log](docs/architecture-decision-log))

### 1.2 Out of Scope

- Business requirements gathering and analysis, domain exploration/distillation
- DDD **strategic** patterns (the bounded contexts are taken as given,
  mirroring the reference project)
- Infrastructure, containerization, deployment, maintenance
- A tiered (separate-web-host) frontend — the sample ships the single-host
  Razor + HTMX companion UI only (see [§3.10](#310-ui-companion-single-host));
  tiered mode is intentionally deferred per the Modulus.UI design guide
- Production identity (Authentik is the sole IdP; see [Roadmap](#8-roadmap))

### 1.3 Reason

Most sample applications are either trivial CRUD, unfinished, or silent about
the *why* behind their structure. This sample exists to show a complete,
opinionated, production-shaped modular monolith — and to prove that the Modulus
framework carries that shape with little ceremony: explicit module
registration, per-module databases, and framework-provided cross-cutting
concerns.

### 1.4 Disclaimer

Software architecture should always resolve specific **business problems**.
What is presented here is **one of many ways** to solve this problem class.
Take what fits, and always evaluate functional requirements, quality
attributes, team, and constraints before adopting any decision wholesale.

## 2. Domain

### 2.1 Description

The **Meeting Groups** domain follows [Meetup.com](https://www.meetup.com/).
The main business entities are `User`, `Meeting Group` and `Meeting`.

**Meetings.** A `User` can create a `Meeting Group`, join one, or attend a
`Meeting`. A group member is either an `Organizer` or a plain `Member`. Only an
`Organizer` of a paid-up group can create a `Meeting`. A `Meeting` has an
attendee limit (overflow goes to the `Waitlist`), a guest limit, and must have
at least one `Host`. Members can comment on meetings (one-level replies).

**Administration.** To create a group, a member files a `Meeting Group
Proposal`. An administrator accepts or rejects it. On acceptance, an
integration event tells Meetings to create the group.

**Payments.** A `Payer` buys a `Subscription` (covers up to 3 groups, must stay
active for groups to organize meetings) and pays per-meeting `Event Fees`.

**Users.** Every administrator, member and payer is a `User`. Becoming a user
takes two steps: `User Registration`, then confirmation. Authentication lives in
User Access.

### 2.2 Business processes

**User registration.** `POST /api/registrations` → `UserRegisteredIntegrationEvent`
→ User Access creates an inactive `User`. `POST /api/registrations/{id}/confirm`
→ `UserRegistrationConfirmedIntegrationEvent` → User Access activates the `User`.

**Meeting group creation.** `POST /api/administration/proposals` → proposal in
`Proposed` state. Accept → `MeetingGroupProposalAcceptedIntegrationEvent` →
Meetings creates the `MeetingGroup`. Reject → proposal closed, no side effects.

**Meeting organization.** `POST /api/meetings/meetings` enforces both rules at
the domain level: the group must have an active subscription and the creator
must be an organizer. `POST .../join` applies the attendee-limit/waitlist rule;
`POST .../comments` adds discussion.

**Payments.** `POST /api/payments/subscriptions` → `SubscriptionPurchasedIntegrationEvent`
→ Meetings extends the payer groups' paid-through date. `POST
/api/payments/meeting-fees` records an event-fee payment.

## 3. Architecture

### 3.0 C4 Model

C4 sources (PlantUML, diagram-as-text) live in [docs/C4](docs/C4): system
context, container, component (high-level and module-level), and the meeting
aggregate class diagram. Render any of them with PlantUML to get the images.

### 3.1 High Level View

```text
                    +------------------+
                    |   HTTP clients   |
                    +--------+---------+
                             |
              +--------------v---------------+
              |          Meetup.Web          |  thin host: middleware,
              |  (composition root only,     |  module registration,
              |   no business logic)         |  endpoint mapping
              +--+----+----+----+-----+-----+
                 |    |    |    |     |
        +--------v-+  |    |    |     |
        | Registra-+-+    |    |     |  UserAccess: auth + users
        | tions  |  |     |    |     |  Registrations: sign-up flow
        +---+----+  |     |    |     |  Administration: proposals
            |  +----v-----v+   |     |  Payments: subscriptions + fees
            |  | Meetings  |   |     |  Meetings: groups + meetings
            |  +-----------+   |     |
            +------> | <-------+-----+
              integration events (IModuleBus)
```

**Module descriptions:**

- **Meetup.Web** — thin ASP.NET Core host (API-first with UI: `/api/*`
  endpoints plus Razor Pages from the module `.Web` RCLs). Registers modules
  explicitly (`AddModulus(…AddModule<…>…)` in registration order), wires framework
  middleware (correlation, idempotency, security headers, exception handling),
  maps endpoints and health probes. Contains no business logic.
- **UserAccess** — authentication (`AuthenticateCommand`) and user management.
  Consumes registration events to create/activate users.
- **Registrations** — user sign-up and confirmation. Publishes
  `UserRegistered` / `UserRegistrationConfirmed` integration events.
- **Administration** — meeting-group proposals (propose/accept/reject/listing).
  Publishes `MeetingGroupProposalAccepted`.
- **Payments** — subscriptions and meeting-fee payments. Publishes
  `SubscriptionPurchased`.
- **Meetings** — meeting groups, meetings, attendees, comments. Consumes
  proposal-accepted and subscription events.

**Key assumptions:**

1. The API contains no application logic.
2. The API reaches modules through `IMediator` (`SendAsync`/`QueryAsync`)
   from REPR endpoints — one endpoint = one file.
3. Modules never call each other directly; they integrate **only** through
   integration events published on `IModuleBus`
   (see [ADR-0014](docs/architecture-decision-log/0014-event-driven-communication-between-modules.md)).
4. Each module **owns its data** — separate `DbContext`, separate SQLite file
   (separate schemas/databases in production). Shared tables are forbidden.
5. Each module has its own composition root (`{Module}Module`) registering its
   `DbContext`, `IUnitOfWork`, repositories, and mediator handlers.
6. Registration order in `Program.cs` is authoritative for all lifecycle phases.

### 3.2 Module Level View

Each module is a vertical slice of five projects (Clean Architecture, collapsed
to four layers plus the UI companion RCL — DTOs under `Application/Dtos`,
events under `Application/IntegrationEvents`):

| Layer | Contains | Example |
|-------|----------|---------|
| `Domain` | Aggregates, entities, enums, invariants, `I*Repository` contracts | `Entities/Meeting.cs`, `Enums/AttendeeStatus.cs` |
| `Application` | Commands/queries + handlers + validators, DTOs, integration events and their handlers, `IUnitOfWork` | `Commands/CreateMeeting/CreateMeetingHandler.cs` |
| `Infrastructure` | `{Module}Module`, `{Module}DbContext`, design-time factory, EF repositories, migrations | `MeetingsModule.cs`, `MeetingsDbContext.cs` |
| `Presentation` | REPR endpoints, one file per endpoint (request co-located) | `Endpoints/CreateMeetingEndpoint.cs` |
| `Web` (RCL) | `{Module}WebModule` (sidebar contribution), Razor Pages for the module (list/create/confirm screens over the module's mediator handlers) | `MeetingsWebModule.cs`, `Pages/Meetings/Index.cshtml` |

```text
src/
  Web/Meetup.Web/                          # host (single executable: API + UI)
  Shared/Meetup.Shared.{Domain,Application,Infrastructure,Presentation}/
  Modules/
    Administration/Meetup.Modules.Administration.{Domain,Application,Infrastructure,Presentation,Web}/
    Meetings/      Meetup.Modules.Meetings.{Domain,Application,Infrastructure,Presentation,Web}/
    Payments/      Meetup.Modules.Payments.{Domain,Application,Infrastructure,Presentation,Web}/
    Registrations/ Meetup.Modules.Registrations.{Domain,Application,Infrastructure,Presentation,Web}/
    UserAccess/    Meetup.Modules.UserAccess.{Domain,Application,Infrastructure,Presentation,Web}/
```

File conventions (enforced across the sample):

- **One type per file** — except an endpoint and its own request DTO, which are
  co-located in the endpoint file.
- **Use-case folders** — `Commands/<UseCase>/<UseCase>Command.cs` (+ `Handler`,
  + `Validator` when rules exist); same for `Queries/<UseCase>/`.
- **One DTO per file** under `Application/Dtos`, one event per file under
  `Application/IntegrationEvents`, one entity/enum per file under
  `Domain/Entities` / `Domain/Enums`, one repository contract/implementation
  per file.

### 3.3 API and Module Communication

Modules are registered explicitly; order is authoritative:

```csharp
builder.Services.AddModulus(builder.Configuration, modules => modules
    .AddModule<RegistrationsModule>()
    .AddModule<UserAccessModule>()
    .AddModule<AdministrationModule>()
    .AddModule<PaymentsModule>()
    .AddModule<MeetingsModule>());
```

Endpoints are REPR style and talk to the application layer only through
`IMediator`:

```csharp
public sealed class CreateMeetingEndpoint(IMediator mediator)
    : Endpoint<CreateMeetingRequest, Guid>
{
    public override void Configure()
    {
        Post("/api/meetings/meetings");
        AllowAnonymous();
        Summary("Creates a meeting (organizer of a paid group only)");
    }

    public override async Task HandleAsync(CreateMeetingRequest req, CancellationToken ct)
    {
        var id = await mediator.SendAsync(new CreateMeetingCommand(...), ct);
        await SendCreatedAsync(id, $"/api/meetings/meetings/{id}", ct);
    }
}
```

Commands may return results (e.g. the new resource id, the join status string).
See [ADR-0008](docs/architecture-decision-log/0008-allow-return-result-after-command-processing.md).

### 3.4 Module Requests Processing via CQRS

Writes go through command handlers against the rich domain model
(`Meeting.Create(...)` enforces paid-group + organizer rules and throws on
violation); reads go through query handlers projecting to DTOs. Every command
with rules has a FluentValidation validator executed in the mediator pipeline;
each module commits through its own `IUnitOfWork`.

### 3.5 Domain Model Principles

1. **Encapsulation** — private setters, private constructors, static factories.
2. **Persistence ignorance** — no infrastructure references in `Domain`.
3. **Behavior-rich** — invariants live in the entities (`Meeting.Create`,
   `MeetingAttendee.Join`, `MeetingGroupProposal.Accept/Reject`,
   `UserRegistration.Confirm`, `Subscription.Renew`).
4. **Business language** — `Organizer`, `Waitlist`, `Host`, `Proposal`,
   `Subscription` — no CRUD-speak.
5. **Invariants via exceptions** — violations throw (`InvalidOperationException`,
   `ArgumentException`); the framework exception handler maps them to HTTP
   status codes. See [ADR-0010](docs/architecture-decision-log/0010-rich-domain-models.md).

### 3.6 Cross-Cutting Concerns

Provided by the Modulus framework and wired once in `Program.cs`:

- Correlation (`X-Correlation-ID` adopt/echo), HTTP idempotency
  (`Idempotency-Key` with tenant-scoped replay), security headers, global
  exception handling, OpenAPI + Scalar UI, liveness/readiness probes.
- Mediator pipeline: validation + transaction (`TransactionBehavior`) +
  logging around every command/query.

### 3.7 Modules Integration

Publishers emit integration-event records via `IModuleBus`; consumers handle
them in `Application/IntegrationEventHandlers`:

| Event | Publisher | Consumer(s) |
|-------|-----------|-------------|
| `UserRegisteredIntegrationEvent` | Registrations | UserAccess → creates inactive user |
| `UserRegistrationConfirmedIntegrationEvent` | Registrations | UserAccess → activates user |
| `MeetingGroupProposalAcceptedIntegrationEvent` | Administration | Meetings → creates group |
| `SubscriptionPurchasedIntegrationEvent` | Payments | Meetings → extends paid-through date |
| `MeetingGroupCreatedIntegrationEvent` | Meetings | (published fact, no local consumer) |

### 3.8 Security

Every business endpoint requires an Authentik JWT and enforces **both** the
Authentik group role and the module permission (AND semantics; see
[ADR-0013](docs/architecture-decision-log/0013-authentik-pkce-roles-permissions.md)
and [§5](#5-how-to-run)). The Razor companion pages are anonymous admin
screens (no cookie sign-in wired) — they resolve the acting login per form
while the API endpoints resolve it from the JWT.

### 3.9 Tests

No test projects ship with this sample yet (see [Roadmap](#8-roadmap)). The
recommended shape, mirroring the framework's own suite: xUnit +
FluentAssertions, `[Trait("Category", "Unit")]` for domain/handler tests,
`Modulus.Testing` + per-context SQLite for endpoint tests.

### 3.10 UI companion (single host)

The same host serves `/api/*` (REPR endpoints, Bearer JWT) and Razor Pages
(admin screens for each module). Every module owns its UI in a fifth project —
the `Meetup.Modules.{Module}.Web` Razor Class Library — holding the module's
pages (`Pages/{Module}/`), the `{Module}WebModule` sidebar contribution, and
its own `_ViewImports`/`_ViewStart`. Pages call the application layer directly
through `IMediator` in-process — no HTTP round-trip to the sample's own API —
so the same handlers, validation pipeline and transactions serve both shapes.
The host just references the `.Web` projects and registers
`AddUiModule<{Module}WebModule>()`; Razor Pages in RCLs are discovered
automatically and keep their routes (`/Meetings`, `/Registrations`, …).

| Page | Route | Mediator surface |
|------|-------|------------------|
| Registrations | `/Registrations` | `RegisterNewUser` + `ConfirmRegistration`, `GetRegistrations` |
| Users | `/UserAccess` | `DeactivateUser`, `GetUsers` |
| Proposals | `/Administration` | propose/accept/reject, list proposals |
| Meetings | `/Meetings` | group picker + `CreateMeeting` + `JoinMeeting` |
| Payments | `/Payments` | subscriptions + meeting fees |

HTMX patterns (per the Modulus.UI design guide):

- Every page sets `Layout = Model.IsHtmxFragment ? null : "_UiLayout"` —
  full Tabler shell for normal/boosted navigation, fragment for swaps.
- Mutations keep a classic full-page branch (`RedirectToPage`) plus an HTMX
  branch (`HtmxPartial` + `HtmxToast`), via `HtmxPageModel.HandleAsync` which
  maps validation failures to a `422` form re-render.
- `<body hx-boost="true">` makes navigation boosted; the group picker keeps
  filter state in the URL (`hx-push-url`); destructive actions use
  `hx-confirm`, rendered as the shared Tabler modal by `modulus-ui.js`.
- Alpine owns local state only (e.g. the collapsible create form on
  `/Meetings`); Tabler/Bootstrap owns dropdowns/collapse; all assets
  (htmx 2.x, Alpine 3.x, Tabler) are vendored — no CDN.
- Sidebar navigation comes from the module-owned `AddUiModule<{M}WebModule>`
  companions (`src/Modules/{M}/Meetup.Modules.{M}.Web/`), in
  module-registration order.

### 3.11 Architecture Decision Log

Decisions are recorded in [docs/architecture-decision-log](docs/architecture-decision-log)
(0001–0010). Add a new numbered ADR for every significant decision.

### 3.12 Database change management

Each module owns EF Core migrations in its `Infrastructure` project and an
`IDesignTimeDbContextFactory` (`{Module}DbContextFactory`). At startup,
`MigrateModulusDatabasesAsync` migrates when migrations exist, otherwise
creates the schema (SQLite files land next to the API binary):

```bash
# add a migration for one module
dotnet ef migrations add <Name> \
  --project src/Modules/Meetings/Meetup.Modules.Meetings.Infrastructure/Meetup.Modules.Meetings.Infrastructure.csproj \
  --startup-project src/Web/Meetup.Web/Meetup.Web.csproj \
  --context MeetingsDbContext --output-dir Migrations
```

## 4. Technology

| Area | Choice |
|------|--------|
| Runtime | .NET 10 (`net10.0`), C# latest, nullable + implicit usings |
| Web | ASP.NET Core minimal hosting, REPR endpoints (`Modulus.AspNetCore`) |
| Data | EF Core 10, PostgreSQL 16 — one database + schema per module (dbsh SQL-first migrations) |
| Auth | Authentik (sole IdP, PKCE-only public client) + JwtBearer; Authentik groups as roles, colon-style permissions on every endpoint |
| CQRS | `Modulus.Mediator` (`IMediator`, pipeline behaviours; commands declare `[SkipTransaction]` — single-commit handlers) |
| Events | `Modulus.Events` (`IModuleBus`, in-process) |
| Validation | FluentValidation 12 |
| Docs | Microsoft OpenAPI 2.x + Scalar UI (Development) |
| Conventions | `TreatWarningsAsErrors`, one type per file |

## 5. How to Run

Prerequisites: .NET SDK 10.0.109+, Docker (PostgreSQL + Authentik).

```bash
cd samples/Meetup
cp .env.example .env   # local-dev values only; never commit .env
docker compose up -d   # app postgres on :5432, Authentik IdP on :9010
```

Seed Authentik (one-off, creates the `meetup` OAuth2 provider + application,
`meetup-members|organizers|admins` groups and dev users `alice`/`bob`/`carol`):

```bash
# groups, PKCE-only provider (authorization_code + refresh_token),
# application slug == client id == "meetup", users with passwords —
# see docs/architecture-decision-log/0013-authentik-pkce-roles-permissions.md
```

Then build and run the API (listens on `http://localhost:5123`):

```bash
dotnet build Meetup.slnx
dotnet run --project src/Web/Meetup.Web/Meetup.Web.csproj -- --urls http://localhost:5123
```

Or via the NUKE-style entry points (`Clean|Build|Format|Test|Run`,
`Debug|Release`) — thin wrappers over the same `dotnet` commands:

```powershell
./build.ps1                 # Build (Debug)
./build.ps1 -Target Format
```

```bash
./build.sh Build Release
```

Full NUKE (a C# build project, as in the reference repo) was deliberately not
added: with no test projects and no CI matrix it would add restore cost
without payback. Revisit when Roadmap item 2 (tests/CI) lands.

Then open:

- Scalar UI — `https://localhost:<port>/scalar` (Development)
- OpenAPI JSON — `/openapi/v1.json`
- Liveness — `/health/live`, readiness — `/health/ready`

In Development the app creates the five `meetup_*` databases on first boot
(`MigrateOrCreate` mode). In Production it applies migrations instead
(`Migrate` mode).

### Authentication

Every business endpoint requires an Authentik JWT (anonymous → 401,
wrong role → 403). The `meetup` provider is PKCE-only (no secret, no
password grant). To call the API:

1. Create a PKCE pair and open the authorize URL in a browser
   (replace `<CHALLENGE>`):
   `http://localhost:9010/application/o/authorize/?client_id=meetup&redirect_uri=http://localhost:5123/auth/callback&response_type=code&scope=openid profile email&state=xyz&code_challenge=<CHALLENGE>&code_challenge_method=S256`
   Log in as `alice` / `MeetupAlice123!` (member), `bob` /
   `MeetupBob123!` (organizer) or `carol` / `MeetupCarol123!` (admin).
2. Copy `code` from the redirected address bar and exchange it:
   `POST http://localhost:9010/application/o/token/` (form-urlencoded)
   with `grant_type=authorization_code&client_id=meetup&code=<CODE>&redirect_uri=http://localhost:5123/auth/callback&code_verifier=<VERIFIER>`.
3. Send `Authorization: Bearer <access_token>`. The Postman collection
   (`Meetup.postman_collection.json`, Auth folder) documents the same flow.

Try the golden path with curl (replace `$TOKEN_*` per role):

```bash
# 1. register (member token) + confirm (admin token)
curl -X POST localhost:5123/api/registrations -H "Authorization: Bearer $TOKEN_ALICE" -H 'Content-Type: application/json' \
  -d '{"login":"ada","email":"ada@example.com","password":"Pass123!","firstName":"Ada","lastName":"L"}'
curl -X POST localhost:5123/api/registrations/{id}/confirm -H "Authorization: Bearer $TOKEN_CAROL" -H 'Content-Type: application/json' \
  -d '{"id":"{id}"}'

# 1b. verify the integration events crossed modules (wait ~10s for the
# async relay; re-run until the user shows up — first inactive, then active)
curl -s localhost:5123/api/auth/users -H "Authorization: Bearer $TOKEN_CAROL" \
  | grep -o '"login":"ada","email":"[^"]*","isActive":[a-z]*'

# 2. propose a meeting group (organizer token) + accept it (admin token)
curl -X POST localhost:5123/api/administration/proposals -H "Authorization: Bearer $TOKEN_BOB" -H 'Content-Type: application/json' \
  -d '{"name":"DDD Warsaw","description":"...","city":"Warsaw","countryCode":"PL","proposerLogin":"ada"}'
curl -X POST localhost:5123/api/administration/proposals/{id}/accept -H "Authorization: Bearer $TOKEN_CAROL" -H 'Content-Type: application/json' \
  -d '{"id":"{id}"}'

# 2b. verify the group materialized in Meetings (wait ~10s, re-run until it appears)
curl -s localhost:5123/api/meetings/groups -H "Authorization: Bearer $TOKEN_ALICE" | grep -o '"name":"DDD Warsaw"'

# 3. buy a subscription, then create + join a meeting
curl -X POST localhost:5123/api/payments/subscriptions -H "Authorization: Bearer $TOKEN_ALICE" -H 'Content-Type: application/json' \
  -d '{"payerLogin":"ada","price":99,"currency":"USD"}'
# 3b. verify the subscription marked ada's group paid (wait ~10s; no match = event still in flight)
curl -s localhost:5123/api/meetings/groups -H "Authorization: Bearer $TOKEN_ALICE" \
  | grep -o '"creatorLogin":"ada","paymentValidUntil":"[^"]*"'
curl -X POST localhost:5123/api/meetings/meetings -H "Authorization: Bearer $TOKEN_BOB" -H 'Content-Type: application/json' \
  -d '{"groupId":"...","title":"EventStorming intro",...,"creatorLogin":"ada"}'
```

## 6. API Reference

All business routes require a Bearer JWT; the Auth column shows the minimum
Authentik group (permissions are additionally enforced — see ADR-0013).

| Method | Route | Module | Auth |
|--------|-------|--------|------|
| POST | `/api/registrations` | Registrations — register a user | member (permission-only) |
| POST | `/api/registrations/{id}/confirm` | Registrations — confirm | `meetup-admins` |
| GET | `/api/registrations` | Registrations — list | `meetup-admins` |
| GET | `/api/auth/users` | UserAccess — list users | `meetup-admins` |
| POST | `/api/administration/proposals` | Administration — propose a group | `meetup-organizers` |
| POST | `/api/administration/proposals/{id}/accept` | Administration — accept | `meetup-admins` |
| POST | `/api/administration/proposals/{id}/reject` | Administration — reject | `meetup-admins` |
| GET | `/api/administration/proposals` | Administration — list proposals | organizers or admins |
| POST | `/api/payments/subscriptions` | Payments — buy subscription | member (permission-only) |
| POST | `/api/payments/meeting-fees` | Payments — pay event fee | member (permission-only) |
| GET | `/api/payments/subscriptions` | Payments — list subscriptions | `meetup-admins` |
| GET | `/api/meetings/groups` | Meetings — list groups | member (permission-only) |
| GET | `/api/meetings/groups/{groupId}/meetings` | Meetings — list group meetings | member (permission-only) |
| POST | `/api/meetings/meetings` | Meetings — create meeting | `meetup-organizers` |
| POST | `/api/meetings/meetings/{meetingId}/join` | Meetings — join / waitlist | member (permission-only) |
| POST | `/api/meetings/meetings/{meetingId}/comments` | Meetings — comment | member (permission-only) |
| GET | `/api/meetings/meetings/{meetingId}/attendees` | Meetings — list attendees | member (permission-only) |

## 7. Contribution

1. Keep the four-layer module shape and the one-type-per-file convention.
2. Business rules go in `Domain`; orchestration in handlers; never the reverse.
3. Cross-module communication only via new integration events (with a handler
   on the consuming side) — never direct references.
4. Record significant decisions as new ADRs under
   `docs/architecture-decision-log`.
5. `dotnet build Meetup.slnx` must stay at 0 warnings / 0 errors
   (`TreatWarningsAsErrors` is on).

## 8. Roadmap

1. Wire JWT issuance + `[Authorize]`/permission policies (sample is anonymous).
2. Add unit tests (domain invariants, handlers) and endpoint tests
   (`Modulus.Testing` + per-module SQLite).
3. Seed demo data (`IDataSeeder`) for one-command exploration.
4. Postgres profile (one database, five schemas) alongside SQLite.
5. Outbox/inbox between modules for durable delivery.

## 9. Authors

Built on the Modulus framework. Domain inspired by Kamil Grzybek's
[modular-monolith-with-ddd](https://github.com/kgrzybek/modular-monolith-with-ddd).

## 10. License

Same as the Modulus framework — see [../../LICENSE](../../LICENSE).

## 11. Inspirations and Recommendations

- [Modular Monolith with DDD](https://github.com/kgrzybek/modular-monolith-with-ddd)
  — Kamil Grzybek; the domain and much of the architectural thinking here.
- [Clean Architecture](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)
  — Robert C. Martin; the layering inside each module.
- [Domain-Driven Design Reference](http://domainlanguage.com/ddd/reference/) —
  Eric Evans; tactical patterns vocabulary ([glossary](docs/catalog-of-terms)).
- [C4 model](https://c4model.com/) — Simon Brown; the documentation maps.
- [Event Storming](https://www.eventstorming.com/) — Alberto Brandolini;
  the process-first view of the domain.
