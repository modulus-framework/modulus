# Modulus Authorization & Access Control Framework — Architecture Blueprint

> **Status:** Design specification (pre-implementation). This is a functional and
> architectural blueprint — it defines *what* the authorization framework must do
> and *why*, not *how* it is coded. Implementation increments are tracked
> separately in `IMPROVEMENT_PLAN.md`; the first increment (the **permission grant
> store**) is called out in §22.
>
> **Audience:** framework architects and module authors building on Modulus.
> **Scope:** the shared authorization core that every Modulus-based application
> (ERP, CRM, HRM, MES, WMS, procurement, finance, payroll, POS, PM, asset,
> healthcare, education, government, multi-tenant SaaS) inherits and configures.

---

## 1. Executive Summary

Modern enterprise applications fail at security not because they lack checks, but
because their checks are **scattered, inconsistent, and duplicated**. The same
question — *"is this user allowed to do this, to this record, right now?"* — gets
answered in twenty different places with twenty slightly different answers. Over a
product's life this produces privilege-escalation bugs, cross-tenant data leaks,
un-auditable decisions, and a codebase that resists every new organizational
requirement.

This framework centralizes **every authorization decision** behind one consistent,
extensible model. It treats authorization as a first-class platform capability —
like persistence, messaging, or multi-tenancy — rather than a per-feature
afterthought. It answers six orthogonal questions with six composable mechanisms:

| Question | Mechanism |
|---|---|
| *Can this user use this capability at all?* | **Permissions** (capability grants) |
| *Which set of capabilities does this user have?* | **Roles & permission sets** |
| *Where in the organization can they act?* | **Organizational scope** |
| *Which records can they see/touch?* | **Data (row) scope** |
| *Which fields within a record?* | **Field-level security** |
| *Is this specific action valid on this specific object right now?* | **Resource- & workflow-aware policies** |

The design goal is **decisions as data, enforcement as infrastructure**. Business
administrators configure authorization without code changes; developers annotate
intent and never re-implement the decision engine; auditors get a single,
explainable trail of every decision.

The framework is designed to remain valid from a five-user single-company
deployment to a multi-million-user multi-tenant SaaS platform, and to absorb new
organizational models, business policies, and compliance regimes over a 10–15 year
horizon **without redesign** — only configuration and extension.

---

## 2. Vision and Design Philosophy

### 2.1 Guiding principle — *Policy-Based Access Control (PBAC) as the substrate*

Roles and permissions are the **vocabulary** users understand; attributes and
policies are the **engine** that scales. The framework is fundamentally an
**attribute/policy-based** decision system that *presents* a familiar
role-and-permission surface. This is the architecture every mature vendor
converged on after starting with pure RBAC (see §18 anti-patterns and the
benchmark in §17): RBAC for *what you can do*, ABAC/PBAC for *which data and under
what conditions*.

### 2.2 Design tenets

1. **Deny by default, fail closed.** Absence of a grant is a denial. An error in
   resolving context (unknown tenant, unresolved user, misconfigured policy) is a
   denial, never an accidental allow. (Modulus already enforces this at the
   tenancy layer — an unresolved tenant matches *nothing*; authorization inherits
   the same discipline.)
2. **Single decision point, many enforcement points.** There is exactly one
   conceptual **Policy Decision Point (PDP)**. It is *called* from many **Policy
   Enforcement Points (PEPs)** — HTTP endpoints, mediator pipeline, query filters,
   UI projection — but the *logic* lives in one place.
3. **Decisions are data, not code.** Roles, grants, scopes, and policy bindings
   are configuration an administrator edits at runtime. Code declares *what
   capabilities exist* and *what a policy needs to know*; it does not hard-code
   *who* gets them.
4. **Declarative intent over imperative checks.** A developer declares the
   *requirement* ("this operation needs `sales.order.approve` scoped to the order's
   business unit"). The framework decides. `if (user.IsAdmin)` scattered in
   handlers is the anti-pattern this exists to eliminate.
5. **Layered defense (positive + negative).** Coarse capability gates run first
   and cheap; fine-grained data and field checks run last and precise. A user must
   pass *every* layer — capability, scope, record, field, workflow, feature, tenant.
6. **Explainability is a feature.** Every decision can answer *why*: which grant
   allowed it, which rule denied it. This powers auditing, support ("why can't I
   see this?"), and administrator confidence.
7. **Least privilege, expressed naturally.** The default posture and the
   configuration ergonomics must make the *narrow* grant the easy grant.
8. **Performance is a design constraint, not a tuning phase.** Authorization runs
   on the hot path of every request and every row returned. The model must be
   resolvable in-memory, cacheable, and expressible as a database predicate so
   that data-scope filtering happens in the query, not in application memory.

### 2.3 What "world-class" means here

It is *not* maximal feature count. It is the disciplined **separation of a small,
stable core** (the decision contract, the six mechanisms, the enforcement points)
from an **open extension surface** (custom scope providers, policy handlers,
attribute sources). The core rarely changes; the enterprise-specific richness
lives in extensions the core never needs to know about.

---

## 3. Business Problems Addressed

| Problem | How the framework resolves it |
|---|---|
| **Inconsistent rules** — the same permission enforced differently per screen | One PDP; all PEPs delegate to it. Consistency is structural, not disciplinary. |
| **Duplicate permission logic** | Capabilities declared once in a registry; checks reference names, never re-derive them. |
| **Hard-coded security** (`if role == "admin"`) | Roles/grants are data; code declares requirements, not identities. |
| **Complex org hierarchies** | First-class organizational scope model with hierarchical inheritance and configurable traversal. |
| **Cross-module authorization** | Shared vocabulary and decision engine; a document that flows Sales → Inventory → Finance is evaluated by the same PDP in each. |
| **Multi-company / multi-tenant** | Tenant isolation is the outermost, non-negotiable boundary; company/legal-entity scope sits inside it. |
| **Delegated authority** | Delegation is a first-class, time-bounded, auditable grant — not a shared password or a permanent role. |
| **Dynamic business policy** | Policies are attribute-driven and runtime-editable; new conditions are configuration or pluggable handlers, not releases. |
| **Regulatory compliance** | Every decision is explainable and auditable; segregation-of-duties and field confidentiality are modeled, not bolted on. |
| **Future expansion** | New scope types, attributes, and policy handlers plug in without touching the core contract. |

### 3.1 Why role-based security alone is insufficient

RBAC answers *"what can this user do?"* but is blind to *"to which data?"* and
*"under what conditions?"*. Three failures are structural, not fixable by adding
more roles:

- **Role explosion.** Encoding data scope into roles ("Sales-Manager-North",
  "Sales-Manager-North-ExceptForVIPAccounts") multiplies roles combinatorially
  until the role catalog is unmanageable and itself a security risk.
- **No record awareness.** "Can approve orders" cannot express "…but only orders
  under €10,000, in their own business unit, that they did not themselves create"
  (segregation of duties). That is a *data + context* decision.
- **No temporal or conditional dimension.** "Can post to the ledger" must yield to
  "…only while the fiscal period is open." Roles have no concept of *when* or
  *state*.

RBAC is necessary and retained — as the human-friendly capability layer. It is
composed with organizational scope, data scope, and policy conditions to form a
complete decision. This is the RBAC→PBAC evolution every benchmark platform made.

---

## 4. Core Framework Responsibilities

The framework owns, and is solely responsible for:

1. **Authentication context** — a normalized, trustworthy description of the caller
   (identity, tenant, org placement, roles, effective permissions, attributes).
2. **A permission (capability) catalog** — the authoritative, module-contributed
   registry of what capabilities exist in the application.
3. **Grant management** — the assignment of capabilities to principals (users,
   roles, groups) and the resolution of a principal's *effective* permission set.
4. **The decision engine (PDP)** — the single component that combines capability +
   scope + record + field + condition into an allow/deny with a reason.
5. **Enforcement integration (PEPs)** — first-class hooks at the HTTP boundary, the
   application (mediator) pipeline, the data query layer, and the UI projection
   layer, all delegating to the PDP.
6. **Data-scope translation** — turning a user's authorization context into a
   **query predicate** so record filtering happens in the database.
7. **Field-level projection** — masking/omitting fields a user may not see, on
   both read (output) and write (input) paths.
8. **Feature/entitlement gating** — enabling/disabling whole capabilities by
   license, subscription, or tenant policy, *above* per-user permissions.
9. **Delegation & temporary access** — time-bounded, auditable transfer or grant
   of authority.
10. **Audit emission** — a structured, tamper-evident record of grants,
    revocations, and (configurably) decisions.
11. **The extension contract** — stable interfaces through which applications add
    scope providers, policy handlers, and attribute sources.

Explicitly **out of scope** (delegated to adjacent components, consumed by this
one): authentication protocol handling (token issuance/validation), user directory
management, and the workflow *engine* itself (the framework is workflow-*aware* but
does not orchestrate workflows).

---

## 5. Functional Capability Analysis

Each capability below states its **purpose**, **business value**, **enterprise use
cases / real-world examples**, **common challenges**, and **best practices**.

### 5.1 Authentication Context

**Purpose.** Every decision is a pure function of *who is asking* and *what they are
asking about*. The authentication context is the trustworthy "who," assembled once
per request and immutable thereafter.

**What it must carry.** Identity (stable user id, not a display name); tenant
identity and whether the caller is operating as a **host/platform** actor;
organizational placement (which company/business unit/branch/team the user acts
within, including a possibly-multiple set); assigned roles and the **effective**
permission set derived from them; a bag of **attributes** (employment status,
clearance level, region, cost-center, manager-of relationships, licenses); the
authentication strength/method (was this MFA? a service account? an impersonation
session?); and the active delegations in effect.

**Business value.** A decision is only as good as its inputs. Centralizing the
context means every PEP evaluates against the *same* facts, and new decision
factors are added in one place.

**Use cases.** A background job posting outbox events acts as **host** with no user;
a support engineer **impersonating** a customer must be flagged so the audit trail
records both identities; a user who belongs to *two* business units needs both in
scope.

**Challenges.** (a) *Where does authority originate?* Trusting claims blindly means
whoever mints the token controls authorization — acceptable only if the token
issuer is the same trust domain. For fine-grained, editable, data-scoped decisions,
the effective permission set must be **resolved server-side from the grant store**,
not read from a self-asserted claim. (b) *Freshness.* Cached context must expire or
be invalidated when grants change, or a revoked permission lingers. (c) *Ambient
context* (tenant, impersonation) must be explicit and immutable, never mutable
global state.

**Best practices.** Normalize once at the edge into an immutable context object;
resolve effective permissions server-side (claims may *carry* identity and roles,
but data scope and fine-grained grants are resolved against the store); make
host/system and impersonation first-class, explicit flags; never let a PEP reach
around the context to raw claims.

> **Modulus today:** `ICurrentUser` (identity, roles, flat permission list) and
> `ICurrentTenant` (with the fail-closed `IsHost`) already form the spine of this
> context. The gap this blueprint fills: permissions currently arrive as JWT
> *claims* (issuer-asserted). The grant store (§22) makes the effective set
> *server-resolved and editable* — the pivotal step from static to enterprise-grade.

### 5.2 Permission Management

**Purpose.** A permission is the atomic unit of *capability* — the smallest
"you may do X" the system recognizes.

**Permissions represent business capabilities, not technical operations.** The
right permission is `sales.order.approve`, not `UPDATE dbo.Orders`. Business
capabilities are stable, meaningful to administrators, and independent of
implementation; technical operations are volatile and meaningless to the people who
actually assign access. This single choice determines whether your permission
catalog is administrable by a business analyst or only by a developer.

**Organization & lifecycle.**
- **Hierarchical naming** — `module:group:action` (e.g., `sales:order:approve`).
  The prefix enables per-module grouping, wildcard grants (`sales:order:*`), and a
  navigable admin UI. (Modulus already uses `:`-delimited names and module-prefixed
  grouping.)
- **Grouping & categorization** — permissions roll up into functional groups for
  presentation and bulk assignment.
- **Prerequisites/implication** — a permission may *require* or *imply* another
  (approve implies read; you cannot grant approve without read). Modulus's
  `PermissionDefinition.Requires` is the seed of this.
- **Multi-valued/parametric permissions** — richer platforms attach *values* to a
  permission (SAP's authorization objects carry field values: activity 01/02/03,
  org level, amount ceilings). Modulus should support this as a permission carrying
  a small set of **constraint attributes** rather than exploding into many names.
- **Lifecycle** — permissions are declared by modules at startup and **frozen**
  (immutable at runtime) so the catalog is a stable contract; *grants* against them
  are fully dynamic. Deprecation is a first-class state (a permission can be marked
  obsolete without breaking existing grants).

**Business value.** A clean, business-meaningful, self-describing catalog is the
foundation everything else assembles from. Get this wrong and every role, every
audit, every admin screen inherits the mess.

**Challenges.** Granularity balance — too coarse and you cannot express least
privilege; too fine and administration drowns. Rule of thumb: one permission per
*business action a user would recognize*, with data/field nuance handled by the
scope/field layers, not by minting more permissions.

**Best practices.** Declare permissions in code (discoverable, versioned,
refactor-safe), assign them as data. Freeze the catalog after startup. Prefer
implication chains over duplicate grants. Reserve permission *values/constraints*
for genuinely parametric capabilities.

### 5.3 Role-Based Authorization

**Purpose.** A role is a **named bundle of permissions** matching a job function
("Accounts Payable Clerk", "Warehouse Supervisor"). Roles are how humans reason
about and assign access at scale — nobody assigns 300 permissions one at a time.

**Where RBAC excels.** Job-function-aligned access; bulk assignment and revocation;
comprehensible administration; a natural unit for segregation-of-duties analysis
("no one may hold both *create-vendor* and *approve-payment*").

**Where RBAC breaks (and must be composed, not extended).** The moment
authorization depends on *which record* or *what condition*, roles alone force
either role explosion or coarse over-granting. See §3.1. The framework's answer is
**not** more roles — it is to keep roles as the capability layer and delegate data
and conditions to the scope and policy layers.

**Design refinements the framework adopts:**
- **Roles as data, hierarchical.** Roles can inherit (Senior Manager ⊇ Manager ⊇
  Employee), reducing duplication. Guard against deep, tangled hierarchies (a known
  anti-pattern — §18).
- **Permission sets / entitlement bundles.** A finer-grained, composable unit
  between permission and role (Salesforce's permission sets, Dynamics's stacked
  roles): additive bundles layered onto a base role so you avoid cloning a whole
  role to add one capability. This is the primary escape valve from role explosion.
- **Static vs. dynamic roles.** Static: assigned by an admin. Dynamic/computed:
  membership derived from attributes ("all users whose department = Finance"),
  re-evaluated as attributes change — powerful but must be explainable.
- **User-level grants/denies.** Direct grants and, importantly, **explicit denies**
  that override role grants (needed for compliance exceptions). Deny-overrides-allow
  must be an explicit, audited, deliberately-scarce mechanism.

**Best practices.** Keep roles business-function-aligned and *free of data scope*.
Use permission sets for the long tail. Keep role hierarchy shallow. Make role
assignment auditable and, for sensitive roles, approval-gated. Analyze roles for
segregation-of-duties conflicts as a standing control.

### 5.4 Organizational Authorization

**Purpose.** Enterprises are structured — companies, legal entities, business
units, branches, departments, teams, warehouses, factories, cost centers, regions,
projects, buyer groups. Authorization must understand *where in that structure* a
user is entitled to act.

**The model.** A configurable **organizational hierarchy** (a tree, sometimes a
DAG — a user or unit may report into more than one parent, e.g., a matrixed
functional + geographic structure). Each user has one or more **organizational
placements**. Permissions are evaluated *relative to* a scope in this tree.

**Hierarchical access models (the traversal choices):**
- **This unit only** — act strictly within the assigned node.
- **This unit and descendants** — a regional manager sees their region and every
  branch beneath it (downward inheritance — the most common enterprise model).
- **This unit and ancestors** — rare; visibility "upward" for roll-up reporting.
- **Sibling/peer** — shared visibility across units at the same level.
- **Cross-hierarchy assignment** — explicit grants to units outside one's branch
  (a shared-services team serving multiple divisions).

**Business value.** This is what lets one role definition ("Branch Manager") serve
500 branches: the *capability* is identical; the *scope* is the manager's own node.
It collapses the role explosion that pure RBAC would suffer.

**Use cases.** A cost-center owner approves spend only within their center and
sub-centers; a factory supervisor sees work orders for their plant; a regional
sales director rolls up all territories beneath them but not peer regions.

**Challenges.** Reorganizations (units merge, split, move) must not orphan grants —
scope references must survive restructuring. DAG hierarchies make "descendants"
ambiguous and expensive; caching the hierarchy closure is usually required. Users
with multiple placements need well-defined *union* semantics.

**Best practices.** Model the hierarchy as first-class data with a cached
transitive closure; support downward inheritance as the default; make traversal
mode a property of the *grant* (this role, at this scope, with this traversal), not
a global setting; design for reorg from day one (stable unit identities, grants
that reference units not paths).

**This is distinct from tenancy.** Tenant = the outermost hard isolation boundary
(see §5.12/§13). Organization = structure *within* a tenant. A multi-company group
is typically *one tenant, many companies* (shared platform admin, controlled
inter-company visibility); independent customers are *separate tenants* (no
visibility, ever).

### 5.5 Data Scope Authorization

**Purpose.** Given a user *can* perform an action (capability) *somewhere* (org
scope), **which specific records** may they act on? This is row-level security and
it is where most real enterprise access rules actually live.

**Common visibility models (from narrowest to broadest):**
- **Own records** — records the user created or is the assigned owner of (a sales
  rep sees their own opportunities).
- **Team records** — records owned by the user's team/group.
- **Department / business-unit / branch / company records** — records belonging to
  an organizational node the user is scoped to (composes with §5.4).
- **Assignment-based** — records to which the user is explicitly related:
  assigned customers, assigned suppliers, managed warehouses, buyer's own POs, a
  case worker's caseload, a doctor's patients. This *relationship* is the scope.
- **Hierarchy/management-based** — a manager sees records owned by anyone reporting
  to them (recursively).
- **Shared/collaborative** — records explicitly shared with the user or a group
  (Salesforce sharing rules, Dynamics access teams) — additive, manual grants on
  top of the base model.
- **Global/administrative** — no row restriction (reserved, audited, rare).

**How it must work architecturally.** Data scope is expressed as a **predicate**
that the framework composes into the data query, so the database returns only
authorized rows. It must *never* be "load everything, filter in memory" — that
leaks (via counts, pagination totals, aggregates) and does not scale. This mirrors
how Modulus already implements tenant isolation and soft-delete as EF Core global
query filters and how `GetByIdAsync` deliberately uses a filtered LINQ query
instead of `FindAsync` to avoid bypassing those filters. Data scope is the same
mechanism generalized: **the user's scope becomes a filter.**

**Business value.** This is the difference between "the sales team can read orders"
(useless — which orders?) and a real policy. It is also the primary
data-confidentiality and privacy control (a nurse sees only their ward's patients).

**Challenges.** *Performance* — scope predicates must be index-friendly; a
"management hierarchy" scope can become an expensive recursive join and usually
needs a materialized closure or denormalized owner-path. *Composition* — a user's
effective scope is often a *union* of several models (own + team + explicitly
shared), which must translate to an efficient `OR` of predicates without exploding
into a query the database cannot optimize. *Aggregates and reporting* — the same
scope must apply to counts, sums, and dashboards, not just list screens.
*Bypass paths* — every read path (search, export, API, report) must go through the
filter; the `FindAsync`-bypass lesson generalizes.

**Best practices.** Model scope as composable predicate providers keyed by entity;
apply them as query filters at the data layer, uniformly, with no opt-out on read
paths; provide a *deliberate, audited* elevation for the rare admin/global case;
denormalize ownership/org paths onto records to keep predicates index-friendly;
test scope with data-driven fixtures per model.

### 5.6 Business Operation Authorization

**Purpose.** Beyond CRUD, enterprise systems are defined by **business operations** —
approve, reject, cancel, post, receive, issue, reverse, close, reopen, export,
import, reconcile. These are the high-value, high-risk actions, and they are where
segregation of duties and compliance concentrate.

**Why operational permissions depend on context, not static grants alone.**
"Can approve" is rarely absolute. It is bounded by: an **amount/threshold** (approve
up to €10,000; above that escalates); **segregation of duties** (cannot approve a
document you created or a vendor you set up); **document state** (can only post a
*submitted* document); **period state** (can only post while the fiscal period is
open); **org scope** (approve only within your cost center). A static "approve"
permission is the *entry ticket*; the actual decision consults record and context.

**Business value.** These controls *are* the internal-control and audit posture of
the business. Getting them right is the difference between passing and failing an
SOX/financial audit.

**Use cases.** Multi-tier purchase approval by amount; four-eyes principle on
journal posting; "maker cannot be checker" on payments; period-close locking; export
of PII requiring an elevated, logged permission.

**Best practices.** Model each business operation as its own permission (never fold
"approve" into "edit"). Express thresholds and SoD as **policy conditions** attached
to the operation, evaluated against the record and the actor — not as extra roles.
Treat state-dependent operations under workflow-aware authorization (§5.8). Log
every sensitive operation decision, allow *and* deny, with the deciding factor.

### 5.7 Resource-Based Authorization

**Purpose.** The decision depends on the **specific object instance** and the
relationship between it and the user — not just the object's type. "Can edit
*this* document" because you own it, are responsible for it, or it is in your unit.

**Determining factors.** Ownership (creator/assignee); responsibility (current
handler, approver in the chain); organizational placement of the record;
workflow stage; and confidentiality/classification of the specific instance.

**Relationship to data scope.** Data scope (§5.5) is resource-based authorization
applied *in bulk* to filter a *set*; resource-based authorization is the *same
question* asked about a *single, already-identified* instance ("can this user open
record #4711?"). The framework must answer both with the *same rules* so a record a
user cannot see in a list cannot be opened by guessing its id — the list filter and
the single-item check must never diverge. (Again mirrored by Modulus's insistence
that `GetByIdAsync` honor the same query filters as `ListAsync`.)

**Best practices.** Derive the single-item check from the same predicate/policy as
the collection filter; make ownership/responsibility explicit attributes on the
record; treat instance-level confidentiality as an attribute the policy reads.

### 5.8 Workflow-Aware Authorization

**Purpose.** A business document is not a static row — it moves through states
(Draft → Submitted → Approved → Posted → Closed → Archived; or Rejected/Cancelled).
*Who may do what* changes at each state.

**Examples.** An author edits a *Draft* freely; once *Submitted*, they can no longer
edit but the approver can approve/reject; once *Approved/Posted*, no one edits and
only a privileged role may *reverse*; a *Closed* period is read-only to all but a
reopen-privileged controller.

**Business value.** State-aware rules encode the actual control lifecycle of the
business and prevent the classic failures ("someone edited an approved invoice",
"a posted entry was silently changed").

**Design.** The framework is workflow-*aware*, not a workflow *engine*. It reads the
record's current state as an **attribute** and lets policies condition on it
("`edit` allowed only when state ∈ {Draft, Rejected}"). Permissible **transitions**
are themselves authorization decisions (who may move Submitted → Approved). State
comes from the domain/workflow module; the framework consumes it.

**Challenges.** Keeping state-permission matrices comprehensible as states and
roles grow (a state × role × action matrix); ensuring the transition guard and the
post-transition edit rules agree; concurrent transitions.

**Best practices.** Model the allowed (state, action) pairs as declarative policy
data, not `switch` statements in handlers; treat every transition as a permissioned
operation; surface the state-permission matrix to administrators for review.

### 5.9 Field-Level Security

**Purpose.** Two users may both legitimately open the *same record* yet be entitled
to *different fields* of it. A recruiter sees a candidate's profile but not the
salary offer; a sales rep sees the customer but not the account's margin; a clerk
sees an employee record but not the compensation or the confidential HR notes.

**Business value.** Enables *shared records with differential disclosure* —
essential for privacy (PII/PHI minimization), commercial confidentiality (cost,
margin, pricing), and management information (performance notes, comp). Without it,
you are forced to split records or over-restrict whole documents.

**Two directions, both required.**
- **Read (output) masking** — unauthorized fields are omitted or masked in every
  projection (API responses, reports, exports), never merely hidden in one UI.
- **Write (input) protection** — a user who cannot see/edit a field cannot set it;
  attempts are rejected or ignored, never silently persisted.

**Design.** Field visibility is driven by **field security profiles** (Dynamics's
term) or field-level policies keyed by (entity, field, principal-context). Fields
carry a **classification** (public, internal, confidential, restricted); a user's
clearance/permissions determine which classifications they may read/write. Masking
happens at the serialization/projection boundary so it is uniform across every
output path.

**Challenges.** *Completeness* — every output path must mask (the export and the
report are the usual leaks). *Performance* — per-field checks across large result
sets must be resolved from cached profiles, not per-row DB lookups. *Aggregates* —
can a user who cannot see individual salaries see their *sum*? (A deliberate policy
choice the framework must let you express.) *Search* — filtering on a field a user
cannot see can leak its value through result membership.

**Best practices.** Classify fields declaratively on the model; drive masking from
profiles resolved once per request; enforce at the projection boundary for read and
at the command/validation boundary for write; treat aggregate exposure as an
explicit policy; audit access to restricted fields.

### 5.10 User-Experience Authorization

**Purpose.** Authorization should not only *block* — it should *shape* the interface
so users see a coherent application containing only what they can actually use.
Hidden menus, disabled or absent action buttons, filtered report/dashboard lists,
context-aware commands, personalized workspaces.

**Business value.** Usability, reduced training and support ("why is this button
here if it errors?"), reduced attack surface (undiscoverable ≠ secure, but it
lowers casual probing), and a cleaner, role-appropriate product experience.

**The cardinal rule: UX authorization is convenience, never the control.** Hiding a
button is a courtesy; the *server-side PEP* is the security boundary. Every action
the UI offers must be independently enforced on the server, because the UI is
attacker-controlled. The framework must expose the *same* decision engine to the
UI (so the client can ask "may I?" to decide what to render) **and** enforce it on
the server (so a forged request fails). Two evaluations, one rule source.

**Design.** A capability-query endpoint/contract lets the client fetch the user's
effective permissions/decisions for a screen and render accordingly; the server
enforces regardless. Menu/feature visibility composes permission + feature gate
(§5.11) + scope.

**Best practices.** Never rely on hidden UI for security; drive UI and server from
the same permission source; batch capability queries per screen for performance;
degrade gracefully (disabled with explanation > mysteriously absent, for
discoverability where appropriate).

### 5.11 Feature Management (Entitlements)

**Purpose.** A dimension *above* per-user permissions: whether a **capability,
module, or feature exists for this organization at all** — governed by licensing,
subscription tier, purchased add-ons, organizational policy, or geographic/regulatory
availability.

**Distinction from permissions.** Feature/entitlement gating asks *"is this feature
available to this tenant/organization?"*; permissions ask *"may this user use an
available feature?"*. A feature disabled by entitlement is invisible and
inaccessible to **everyone** in that tenant, including its admins — the check sits
*outside and before* the user-permission check. (This is Modulus's natural
extension of tenancy: a per-tenant feature/entitlement state.)

**Business value.** Monetization (tiered SaaS plans, add-on modules), compliance
(disable a feature not permitted in a jurisdiction), phased rollout (feature flags),
and per-tenant configuration.

**Use cases.** "Advanced analytics" only on the Enterprise plan; "e-invoicing" only
for tenants in countries that mandate it; a customer who purchased Manufacturing but
not Payroll never sees Payroll; a feature dark-launched to 5% of tenants.

**Best practices.** Model features/entitlements as a per-tenant (and optionally
per-org-unit) enabled set, resolved with tenant context; evaluate the feature gate
*before* permission checks and *before* rendering; keep the feature catalog, like
the permission catalog, module-declared; allow hierarchical defaults (plan →
tenant → unit overrides).

### 5.12 Multi-Tenant Security

**Purpose.** Secure, absolute isolation between tenant organizations, while allowing
**platform (host) administration** across tenants and **controlled sharing** of
genuinely-common resources (master reference data, shared catalogs).

**The non-negotiable boundary.** Tenant isolation is the *outermost* check and it
**fails closed**: an unresolved or ambiguous tenant context grants access to
*nothing*. Modulus already implements exactly this — `ICurrentTenant.IsHost`, a
query filter of the form `IsHost || row.TenantId == currentTenant.TenantId` (a null
tenant matches no row), and host-written outbox rows dispatched under an explicit
host scope. The authorization framework treats this as an inviolable precondition:
*no authorization decision is even evaluated until tenant scope is established.*

**Host/platform administration.** A host actor (no tenant, `IsHost = true`) can
administer across tenants — provisioning, support, cross-tenant reporting. This is a
deliberate, audited elevation, not the default, and impersonation of a tenant user
by a host must be flagged in the audit trail with both identities.

**Controlled sharing.** Some data is legitimately shared (a global product taxonomy,
country/currency reference data). The framework must allow *explicitly-marked* shared
resources readable across tenants while everything else is isolated by default — the
sharing is opt-in per resource, never a relaxation of the global filter.

**Best practices.** Tenant filter first and unconditional; fail closed on
unresolved tenant; host access explicit and audited; shared data opt-in and marked;
never let an org/data-scope rule *widen* beyond the tenant boundary.

### 5.13 Temporary and Delegated Access

**Purpose.** Authority is not always permanent. Real organizations need: a manager
on leave **delegating** approval authority to a deputy; **temporary** project-based
access that auto-expires; **emergency/break-glass** access granted under exception
with heightened logging; time-limited elevated admin privileges (just-in-time
admin).

**Why first-class.** The failure mode this prevents is the endemic enterprise
anti-pattern of *sharing credentials* or *granting a permanent role "just for this
week"* that is never revoked — the single largest source of privilege creep. Making
delegation a proper, **time-bounded, auditable, revocable** grant eliminates the
incentive to do it the dangerous way.

**Models.**
- **Delegation** — user A authorizes user B to act with (a subset of) A's authority,
  for a bounded window, optionally scoped to specific documents/amounts. The audit
  trail records B acted *on behalf of* A.
- **Temporary grant** — a role/permission with a validity window; automatically
  inert after expiry (no manual cleanup, no lingering privilege).
- **Break-glass** — pre-defined emergency elevation, self-service but heavily
  logged and typically alerting/ reviewed after the fact.
- **Just-in-time elevation** — a privileged capability granted only for the duration
  of an approved task/session.

**Challenges.** Sub-delegation (can B further delegate?) must be bounded;
delegation must respect the delegator's *own* limits (you cannot delegate authority
you do not have, nor beyond your threshold); expiry must be enforced at decision
time (never rely on a cleanup job alone); revocation must be immediate.

**Best practices.** Every temporary/delegated grant carries an explicit validity
window enforced at the PDP; delegated authority is capped by the delegator's own;
"on behalf of" is preserved end-to-end in the audit trail; break-glass alerts and is
reviewed; default deny on any ambiguity about whether a delegation is still valid.

### 5.14 Auditing and Compliance

**Purpose.** Authorization is a primary source of the accountability record.
*Who was granted what, by whom, when; who accessed what; who was denied what.*

**What to capture.**
- **Administrative events** — grant/revoke of roles and permissions, role
  definition changes, delegation creation, feature toggles. These are the
  highest-value audit records (they explain *why* someone had access).
- **Access decisions** — configurably, allow/deny of sensitive operations and
  restricted-field access, with the *deciding factor* (which grant, which rule).
  Auditing *every* decision is prohibitively voluminous; the framework must let you
  scope decision-auditing to sensitive resources/operations.
- **Effective-access snapshots** — the ability to answer, at a point in time, "what
  could this user access?" and "who could access this record?" (a common auditor
  and breach-investigation question).

**Business value.** Regulatory compliance (SOX, GDPR/HIPAA access controls, ISO
27001, industry regimes); security investigation (what did a compromised account
reach?); governance (periodic access recertification — "confirm each user still
needs their access"); segregation-of-duties attestation.

**Best practices.** Emit structured, tamper-evident audit events for all
administrative changes unconditionally; make decision-auditing declaratively
scoped; retain enough to reconstruct historical effective access (or snapshot
periodically); support access *recertification* campaigns; ensure the audit trail
itself is subject to strict, separate access control and is append-only. Modulus's
existing outbox/integration-event infrastructure is the natural transport for
durable, ordered audit emission.

### 5.15 Extensibility

**Purpose.** No framework can anticipate every organization's structure, policy, or
compliance regime. The measure of longevity is whether a new requirement is a
*configuration/extension* or a *redesign*.

**The extension surface (stable contracts the core depends on, applications
implement):**
- **Custom scope providers** — a new data-scope model (e.g., "records for customers
  in my assigned sales territory") plugs in as a predicate provider without touching
  the engine.
- **Custom policy handlers** — a new business condition (e.g., "block if the
  counterparty is on a sanctions list") plugs in as a policy the engine invokes.
- **Custom attribute sources** — new facts about the user or record (clearance,
  certification, risk score) feed the context and become available to policies.
- **Custom organizational models** — a new hierarchy type or traversal rule.
- **Pluggable stores** — grant/role/delegation persistence is behind an interface so
  it can be backed by the app's database, an external IdP/directory, or a policy
  service, without changing callers.

**Design tenet.** The core defines a *small, stable* contract (the PDP interface,
the six mechanisms, the enforcement points, the extension interfaces). Everything
enterprise-specific is an implementation of those interfaces the core never
references directly. This is precisely the module/extension philosophy Modulus
already applies elsewhere (module contribution, `TryAdd`-based overridable defaults,
provider interfaces).

**Best practices.** Extend by implementing interfaces, never by forking the engine;
keep custom logic in policy/scope/attribute providers, not in enforcement points;
version the extension contracts conservatively; ship sensible defaults that most
apps never replace.

---

## 6. Enterprise Authorization Models (how the mechanisms combine)

The framework is not "RBAC" or "ABAC" — it is a **layered composition** that
presents RBAC and evaluates as PBAC:

```
Tenant isolation        (hard boundary — fail closed, evaluated first)
   └─ Feature/entitlement gate      (is the capability available to this org?)
        └─ Capability check (RBAC)  (does the user hold the permission?)
             └─ Organizational scope (is the user entitled at this org node?)
                  └─ Data/row scope  (is THIS record in the user's scope?)
                       └─ Resource/workflow policy (is the action valid on THIS
                          instance, in THIS state, under THESE conditions?)
                            └─ Field-level projection (which fields, in/out?)
```

A request must satisfy **every** layer. Layers are ordered cheap-to-expensive and
coarse-to-fine so the common denial happens early and cheaply. Each layer is
independently configurable and independently testable. The layered composition is
the design's core intellectual property — it is what lets a *single* role
definition, plus data configuration, express access rules that pure RBAC could only
express through combinatorial role explosion.

---

## 7. Authorization Decision Flow

A single conceptual decision (the PDP), consulted by every PEP:

1. **Establish context.** Resolve tenant (fail closed if unresolved); assemble the
   immutable authentication context (identity, org placements, roles → effective
   permissions, attributes, active delegations, host/impersonation flags).
2. **Feature gate.** Is the requested capability *available* to this tenant/org? If
   not → deny (feature not entitled).
3. **Capability check.** Does the effective permission set include the required
   permission (directly, via role, via delegation, honoring explicit denies)? If
   not → deny (forbidden).
4. **Scope resolution.** For a *collection*: compose the org + data-scope predicate
   and hand it to the query (filtering happens in the database). For a *single
   instance*: evaluate the same predicate against the identified record.
5. **Resource/condition policy.** Evaluate instance-, state-, and
   condition-dependent policies (threshold, SoD, workflow state, confidentiality).
   If any denies → deny (with reason).
6. **Field projection.** Determine readable/writable fields for the context; apply
   masking on output and protection on input.
7. **Decide & explain.** Return allow/deny plus the deciding factor; emit audit as
   configured.

**Deny-override semantics:** an explicit deny at any layer beats any allow. Absence
of a grant is a deny. Errors are denies. The flow is short-circuit: the first deny
stops evaluation (except that auditing may still record it).

---

## 8. Organizational Security Concepts

- **Hierarchy as first-class data** with a cached transitive closure for efficient
  descendant/ancestor queries.
- **Placements**: a user↔unit assignment carrying the role(s) and traversal mode
  effective at that placement; a user may have several.
- **Scope traversal** as a property of the grant (unit-only / unit+descendants /
  etc.), not a global switch.
- **Reorganization resilience**: grants reference stable unit identities; moving a
  unit moves its subtree's effective access with it; splits/merges are supported
  operations, not data-repair emergencies.
- **Matrix organizations**: support a DAG where a unit has multiple parents
  (functional + geographic), with explicit union semantics for effective scope.
- **Company/legal-entity** sits at the top of the org tree *within* a tenant,
  enabling multi-company deployments with controlled inter-company visibility.

---

## 9. Data Visibility Models

The catalog of row-scope models the framework ships, all composable as a union and
all translatable to a query predicate:

| Model | Scope predicate (conceptual) | Typical use |
|---|---|---|
| Own | `record.owner = me` | Sales rep's leads |
| Team | `record.owner ∈ my team` | Team pipeline |
| Org unit (+descendants) | `record.orgUnit ∈ my scope subtree` | Branch/region visibility |
| Assignment | `record ∈ my assigned set` (customers, warehouses, caseload) | Account/territory/case work |
| Management chain | `record.owner ∈ reports-to-me*` | Manager oversight |
| Shared/collaborative | `record ∈ explicitly shared with me/my group` | Ad-hoc collaboration |
| Global/admin | `true` (audited, reserved) | Platform administration |

**Selection guidance.** Default to the narrowest model that satisfies the business
need (least privilege); layer *additive* models (own + shared) via union; reserve
global for explicitly-audited administrative roles. The *same* model set applies to
lists, single-item fetches, aggregates, exports, and reports — uniformly.

---

## 10. Business Operation Security

- Every business operation (approve/post/reverse/close/…) is its **own permission**.
- Operational limits (amount thresholds, SoD, four-eyes, period state) are **policy
  conditions** attached to the operation, evaluated against record + actor — not
  encoded as extra roles.
- Segregation of duties is modeled explicitly and analyzable: the framework can
  answer "does any principal hold a conflicting pair of capabilities?" as a standing
  compliance control.
- Sensitive operations are audited (allow and deny) with the deciding factor.

---

## 11. Field-Level Security Strategy

- Fields carry a **classification** (public/internal/confidential/restricted),
  declared on the model.
- **Field security profiles** map (classification/field, principal-context) →
  read/write permission, resolved once per request from cache.
- Enforcement at the **projection boundary** (uniform masking across API, report,
  export) for reads and at the **command/validation boundary** for writes.
- Aggregate exposure of restricted fields is an explicit policy choice.
- Access to restricted fields is auditable.

---

## 12. Workflow Security Strategy

- The framework is workflow-*aware*; it consumes document **state** as an attribute
  from the domain/workflow module.
- Allowed **(state, action)** pairs are declarative policy data, administrator-
  reviewable, not `switch` statements.
- Every state **transition** is itself a permissioned operation.
- The state-permission matrix is surfaced for governance review.

---

## 13. Multi-Tenant Security Strategy

- Tenant isolation is the **outermost, unconditional, fail-closed** boundary,
  evaluated before any other decision (Modulus's existing `IsHost`/tenant-filter
  model is the foundation).
- **Host** administration is an explicit, audited elevation; host impersonation of a
  tenant user records both identities.
- **Shared resources** are opt-in per resource; default is total isolation.
- No org/data-scope rule may ever widen access beyond the tenant boundary.
- Feature entitlements are resolved per tenant (§11/§5.11).

---

## 14. Feature Management Strategy

- Module-declared **feature catalog**, parallel to the permission catalog.
- Per-tenant (optionally per-org-unit) **enabled set**, with hierarchical defaults
  (plan → tenant → unit override).
- Feature gate evaluated **before** permission checks and **before** UI rendering.
- Drives monetization (tiers/add-ons), compliance (jurisdictional availability), and
  phased rollout (flags).

---

## 15. Delegation and Temporary Access Strategy

- Delegation, temporary grants, break-glass, and JIT elevation are **first-class,
  time-bounded, revocable, audited** grants.
- Validity windows are enforced **at decision time**, not by cleanup jobs.
- Delegated authority is **capped by the delegator's own** (and their limits);
  sub-delegation is bounded.
- "On behalf of" identity is preserved end-to-end in the audit trail.
- Break-glass alerts and is reviewed after the fact.

---

## 16. Audit and Compliance Strategy

- **Administrative changes** (grants, role edits, delegations, feature toggles) are
  always audited, tamper-evident, append-only.
- **Decision auditing** is declaratively scoped to sensitive operations/fields, with
  the deciding factor recorded.
- **Effective-access reconstruction** ("what could X access on date D?", "who could
  access record R?") is supported for investigations and audits.
- **Access recertification** campaigns are a supported governance workflow.
- Audit emission rides Modulus's durable outbox/integration-event transport; the
  audit store itself is access-controlled separately.

---

## 17. Benchmark Against Enterprise Platforms

Conceptual comparison — patterns worth adopting and pitfalls to avoid.

| Platform | Core model | Adopt | Avoid / caution |
|---|---|---|---|
| **SAP (ERP/S4)** | Authorization *objects* with field values (activity, org level, amount); roles via PFCG; derived roles for org scope | Parametric permissions carrying values; derived-role pattern for org scoping; deep SoD tooling | Notorious complexity/opacity; authorization design becomes a specialist discipline — keep the surface administrable |
| **Salesforce** | Profiles + permission sets; Org-Wide Defaults + role hierarchy + sharing rules; field-level security | **Permission sets** (additive, escape role explosion); **OWD default-deny then open up** via sharing; explicit field-level security | Sharing-rule sprawl and recalculation cost at scale; many overlapping mechanisms confuse admins |
| **Microsoft Dynamics 365** | Security roles + **business units** + **field security profiles** + **access teams** | Business-unit hierarchy as org scope; field security profiles; access teams for ad-hoc record sharing | Business-unit rigidity historically forced reorg pain — design for reorg resilience |
| **Workday** | **Domain** security policies + **business process** security; contextual security groups | Separating *functional* (domain) from *process/action* security; contextual (attribute-derived) groups | Steep model; power at the cost of a learning curve — invest in explainability |
| **ServiceNow** | Table/field **ACL** rules evaluated in order; script-able conditions | Layered, ordered ACLs; scriptable/pluggable conditions for the long tail | Script-in-ACL becomes unmaintainable — prefer declarative policy, reserve code for genuine edge cases |
| **Oracle / NetSuite / Odoo** | Roles + record-level "restrictions"/rules; subsidiary/company scoping | Record rules as query predicates; subsidiary (company) scoping within a tenant | Per-record rule proliferation; performance of dynamic rule evaluation |

**Cross-industry lessons.**
- *Everyone* started at RBAC and *everyone* added attribute/policy and row/field
  scope. Build PBAC-under-an-RBAC-surface from day one — retrofitting is the single
  most expensive authorization migration.
- **Permission sets / additive bundles** are the proven cure for role explosion.
- **Default-deny then selectively open** (Salesforce OWD) beats default-open then
  restrict — it fails safe and is easier to reason about.
- **Field-level security and org/business-unit scope** are table stakes at
  enterprise scale, not premium add-ons.
- The platforms that suffer are those where authorization became so complex it
  needed *specialists* (SAP) or where mechanisms overlap confusingly (Salesforce's
  several sharing systems). **Administrability and explainability are as important
  as expressiveness.** Optimize the model so a business analyst — not only an
  engineer — can safely operate it.

---

## 18. Common Anti-Patterns and Design Mistakes

- **Scattered checks.** Authorization logic inline in controllers/handlers/queries.
  → One PDP, many PEPs.
- **Hard-coded identities.** `if (role == "Admin")`. → Declare requirements; grants
  are data.
- **RBAC for everything.** Encoding data scope into role names → combinatorial role
  explosion. → Compose RBAC with scope/policy.
- **Filter in memory.** Loading rows then filtering by scope in application code —
  leaks via counts/pagination/aggregates and does not scale. → Scope as a query
  predicate.
- **List/detail divergence.** A record hidden from a list but openable by id. →
  Same rule for collection and instance (Modulus's `FindAsync`-bypass lesson).
- **UI-only security.** Trusting hidden buttons. → Server enforcement is the
  boundary; UI is convenience.
- **Fail open.** Unresolved tenant/user/context granting access. → Fail closed,
  always (Modulus's tenancy model is the template).
- **Permanent "temporary" access.** Standing roles granted "just for now." →
  First-class time-bounded delegation with decision-time expiry.
- **Claims as the source of truth for fine-grained grants.** Whoever mints the token
  controls authorization; grants cannot be edited or revoked without re-issuing
  tokens. → Resolve effective fine-grained permissions and scope **server-side** from
  the grant store.
- **Deep role/hierarchy tangle.** Unbounded role inheritance nobody can reason
  about. → Shallow hierarchies; prefer additive permission sets.
- **Unexplainable decisions.** "Access denied" with no reason. → Every decision
  carries its deciding factor.
- **Technical-operation permissions.** `UPDATE_TABLE_X` instead of business
  capabilities. → Model capabilities users recognize.

---

## 19. Scalability Considerations

- **Decision on the hot path.** Effective permissions must resolve from an
  in-memory, cached, per-request-immutable context — never a per-check DB round
  trip. Invalidate on grant change.
- **Row scope as SQL, not memory.** Scope predicates compose into the query and must
  be **index-friendly**; denormalize owner/org paths onto records; materialize
  hierarchy closures.
- **Union scopes** must produce predicates the database can optimize, not
  pathological `OR` trees — cap and, where needed, precompute effective scope sets.
- **Field masking** resolves from cached profiles, applied at projection — O(fields)
  per row against a cached lookup, not per-row DB access.
- **Hierarchy math** (descendants, management chains) uses cached transitive
  closures, refreshed on structural change, not recursive queries per request.
- **Caching with correctness.** Cache effective grants/roles/scope with explicit
  invalidation on administrative change (grant/revoke/role-edit/reorg). Staleness of
  a *revoked* permission is a security bug — invalidation must be prompt and, for
  revocations, may warrant push invalidation.
- **Multi-tenant fairness.** Per-tenant caches; a large tenant's authorization data
  must not evict a small tenant's or degrade shared decision latency.
- **Scale target.** The model must hold from single-company/5-user to
  multi-tenant/millions — achieved by keeping the decision *resolvable in memory* and
  the data filtering *pushed into the database*, with everything cacheable and
  invalidatable.

---

## 20. Long-Term Evolution Strategy

- **Stable core, open edges.** The PDP contract, the six mechanisms, the enforcement
  points, and the extension interfaces are the *stable* core. Enterprise richness
  arrives through scope providers, policy handlers, attribute sources, and pluggable
  stores — none of which change the core.
- **Additive, not breaking.** New scope models, attributes, and policies are added;
  existing configurations keep working. Wire/stored formats (grants, audit) get
  forward-compatible evolution, mirroring Modulus's outbox/event versioning
  discipline.
- **Externalization path.** The single PDP is the seam that lets a deployment later
  externalize decisions to a dedicated policy service (OPA-style) or integrate an
  external IdP/policy engine — because callers already depend only on the PDP
  contract, not on where decisions are computed.
- **Standards posture.** Concepts map cleanly to industry directions (PBAC/ABAC,
  externalized authorization, policy-as-data) so the framework can interoperate
  without rewrites.
- **Governance grows with it.** Recertification, SoD analysis, and effective-access
  reporting are designed in, so the framework matures into a *governance* tool, not
  just an enforcement one.

---

## 21. Recommendations — Building for the Next 10–15 Years

1. **Commit to PBAC-under-RBAC now.** Present roles and permissions; evaluate via a
   composable, attribute-aware engine. This single decision prevents the most
   expensive future migration.
2. **One PDP, many PEPs — enforce this structurally.** Make it *impossible* to
   authorize outside the engine by giving developers a declarative surface and no
   convenient alternative.
3. **Fail closed everywhere, inheriting the tenancy discipline** already proven in
   Modulus (`IsHost`, null-matches-nothing, host-scoped dispatch).
4. **Row scope is a query predicate, uniformly applied** across every read path —
   generalize the existing global-query-filter approach.
5. **Resolve fine-grained grants server-side** from an editable, revocable grant
   store; let claims carry identity and coarse roles, not the fine-grained truth.
6. **Model org scope, field security, and workflow state as first-class** from the
   start — they are table stakes, and retrofitting them is disruptive.
7. **Make delegation and temporary access first-class** to kill the permanent-
   "temporary"-access anti-pattern.
8. **Design for administrability and explainability**, not just expressiveness —
   the platforms that suffered made authorization a specialist discipline.
9. **Bake in audit, SoD, and recertification** so compliance is a configuration, not
   a project.
10. **Keep the core small and the extension surface open**, versioned conservatively,
    with defaults most applications never need to replace.
11. **Sequence delivery by dependency and risk** (see §22): the grant store and
    server-side effective-permission resolution first (they unblock everything
    else), then org scope + data scope, then field-level and workflow, then feature
    entitlements, delegation, and the governance/audit tooling.

---

## 22. Alignment with Modulus Today & Delivery Sequence

**What already exists and is reusable as the foundation:**
- `IPermissionRegistry` + `PermissionDefinition` (with `Requires` implication) —
  the **capability catalog** (§5.2). Module-declared, frozen at startup: correct.
- `ModulusPermissionPolicyProvider` + mediator `AuthorizationBehavior` +
  `[RequirePermission]` — **enforcement points** (§4.5) at the HTTP and application
  pipeline layers.
- `ICurrentUser` / `ICurrentTenant` (with fail-closed `IsHost`) — the spine of the
  **authentication context** (§5.1) and the **tenant boundary** (§5.12/§13).
- EF Core global query filters (tenant + soft-delete) and the deliberate
  `GetByIdAsync`-uses-filtered-query decision — the **data-scope mechanism**
  (§5.5/§5.7) in embryonic form, ready to generalize.

**The decisive gap:** effective permissions arrive as issuer-asserted JWT *claims*.
There is no **grant store** — no editable, revocable mapping of *which principal
holds which capability*, no roles-as-data, and therefore none of the org/data/field
layers that build on server-resolved grants.

**Delivery sequence (each increment independently shippable, tested, deny-by-default):**

1. **Permission grant store** *(next increment — the current P1 item).* Roles and
   grants as data; server-side resolution of a principal's **effective permission
   set** (roles → permissions, honoring `Requires` implication and explicit denies);
   `ICurrentUser.HasPermission` resolves against this store (claims still carry
   identity/roles). Unblocks everything below. Fail-closed; frozen catalog, dynamic
   grants.
2. **Organizational scope** — org hierarchy as data, placements, traversal modes.
3. **Data (row) scope** — composable predicate providers generalizing the existing
   query-filter mechanism; union semantics; index-friendly.
4. **Resource/workflow-aware policies** — instance/state/threshold/SoD conditions.
5. **Field-level security** — classifications + profiles, projection-boundary masking.
6. **Feature entitlements** — per-tenant feature catalog and gate.
7. **Delegation & temporary access**; then **governance tooling** (SoD analysis,
   recertification, effective-access reporting) atop the audit stream.

Each increment obeys the working agreements in `IMPROVEMENT_PLAN.md`: builds
0 warnings / 0 errors under global `TreatWarningsAsErrors`, ships a regression test
per behavior, and preserves fail-closed semantics.

---

*End of blueprint. This document governs the design of Modulus authorization; code,
schemas, and APIs are produced per-increment against it and reviewed for
conformance to §2 (tenets), §6 (layered model), and §7 (decision flow).*
