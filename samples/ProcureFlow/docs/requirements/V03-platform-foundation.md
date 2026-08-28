# V03 — Platform Foundation: Multi-Tenancy · Organization Structure · User & Identity · Permission Framework
Covers master topics: 03, 04, 05, 06, 125 (MFA), 126 (SSO), 127 (IP Restriction), 128 (Session Management)

---

# PART A — MULTI-TENANCY (topic 03)

## A1. Objectives & Problems
One codebase, one fleet, thousands of tenants; hard isolation guarantees strong enough for competing RMG groups on the same cell; per-tenant customization (config, workflow, theme) without forks; per-tenant cost attribution for FinOps.

## A2. Isolation Model
**Pool model:** shared PostgreSQL cluster + shared schema + `tenant_id UUID NOT NULL` on every tenant-scoped table + **Row-Level Security** as enforcement backstop.

```sql
ALTER TABLE proc.purchase_order ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON proc.purchase_order
  USING (tenant_id = current_setting('app.tenant_id')::uuid);
-- app role has NOBYPASSRLS; migrations role separate.
```
Per-request: middleware resolves tenant from token → opens tx → `SET LOCAL app.tenant_id = ...` → all queries constrained. Defense-in-depth chain: edge (subdomain/token claim) → app guard (repository base enforces tenant filter) → RLS → per-tenant OpenSearch filtered alias → per-tenant S3 prefix + KMS DEK → audit anomaly detection (cross-tenant query attempts alert).

**Cells:** a cell = DB cluster + Redis + service fleet. Tenants pinned to cells; Enterprise/Group can buy dedicated cells (silo option) with identical code path — isolation level is deployment config, not code branches.

## A3. Tenant Context Object
Resolved once per request, cached in Redis (TTL 5 min, invalidated on change): `{tenant_id, cell_id, plan, entitlements(flags), fiscal_calendar, base_currency, locale defaults, tolerance policies, DoA version, theme}`.

## A4. Tenant-Scoped vs Platform-Scoped Data
Platform-scoped (no tenant_id, read-only to tenants): HS tariff master, SRO registry, currency list, Incoterms, country/port registries, holiday templates, workflow step type registry. Tenant-scoped: everything transactional + tenant overrides of reference data (e.g., preferred duty treatment policies) stored as deltas, never mutations of platform rows.

## A5. Business Rules
MT-01 no API path may accept tenant_id from client payload — derived from token only. MT-02 background jobs carry tenant context explicitly; fan-out jobs iterate tenants with per-tenant tx. MT-03 cross-tenant analytics only via anonymized platform pipelines with tenant opt-in flag. MT-04 tenant export = complete + machine-readable (JSON lines + attachments) ≤ 72 h SLA. MT-05 deletes are soft + retention-scheduled; crypto-shred at retention end.

## A6. Edge Cases
Tenant plan downgrade with in-flight Enterprise features (grace period, read-only feature state); tenant merger (Group acquires Starter tenant → tenant re-parenting tool with ID preservation); cell migration (logical replication cutover playbook, < 15 min freeze); trial abuse (same BIN re-registering — BIN uniqueness check platform-wide).

---

# PART B — ORGANIZATION STRUCTURE (topic 04)

## B1. Hierarchy Model
```
TENANT (group)
 └─ COMPANY (legal entity: BIN, eTIN, IRC/ERC, base currency, fiscal year)
     └─ BUSINESS UNIT (division/SBU)
         └─ SITE (factory/warehouse/office; port-proximity attrs)
             └─ DEPARTMENT
                 └─ POSITION (node users occupy; approval routing anchor)
```
`org.node(id, tenant_id, parent_id, node_type, code, name_en, name_bn, ltree path, effective_from/to, attrs jsonb)` — ltree for ancestor/descendant queries; effective-dating for reorgs.

## B2. Rules & Behaviors
ORG-01 documents stamp company_id + bu_id + site_id + dept_id at creation (denormalized for reporting; validated against org tree version). ORG-02 approval routing resolves "manager-of", "head-of-department", "position X in ancestor chain" against the tree as-of document creation date. ORG-03 reorg = new tree version; in-flight workflows keep their resolution snapshot; new documents use new version. ORG-04 cross-company transactions (Group buying for subsidiaries) require inter-company flag → generates inter-company billing instruction (V18). ORG-05 sites carry customs attributes (nearest port default, bonded warehouse license no for RMG bonded imports).

## B3. Screens
Org tree editor (drag-drop with effect-dating dialog), position manager (occupants, vacancies with delegation fallback), org chart visual export, what-if router ("where would a 75 L PO from Site X route?").

## B4. Edge Cases
Vacant position in approval chain (auto-skip to next level + notify admin; configurable hard-stop); person holding multiple positions (union of permissions, routing per document's org stamp); retroactive org corrections (admin-only, audit-heavy, does not re-route in-flight items); company with multiple IRCs (attribute list, LC validates against selected IRC).

---

# PART C — USER & IDENTITY MANAGEMENT (topic 05) + MFA/SSO/IP/SESSION (125–128)

## C1. Identity Architecture
Keycloak, **single realm**, tenant = organization group `/tenants/{tenant}`; per-tenant IdP federation (SAML/OIDC) mapped to the tenant group (Enterprise SSO, topic 126). Web uses **BFF pattern**: Next.js route handlers hold tokens server-side; browser gets HttpOnly SameSite session cookie; no tokens in JS. APIs use OAuth2 client-credentials (machine) or token exchange (user-context integrations).

## C2. User Lifecycle
Invite (email w/ expiring link) → activate (password policy: 12+ chars, breach-list check | or SSO-only, no local password) → active → role/position assignments → suspend (immediate session revocation) → offboard (reassignment wizard: open tasks, owned documents, delegation transfer) → anonymize after retention (name→"Former User #", audit rows preserved). Service accounts: scoped API clients per integration, secret rotation ≤ 90 days enforced.

## C3. MFA (topic 125)
TOTP baseline (free); WebAuthn/passkeys preferred; SMS OTP fallback (BD numbers, rate-limited, flagged phishable). Policy engine per tenant: enforce-for-all | enforce-for-roles (Finance/Admin default-on) | risk-based step-up (new device, IP country change, high-value approval > configurable BDT threshold triggers re-auth). Recovery: admin-issued one-time codes, identity re-proofing checklist logged.

## C4. SSO (topic 126)
SAML 2.0 + OIDC federation per tenant; JIT provisioning with attribute mapping (email, name, employee_id, department hint) but **roles never auto-mapped from IdP** by default (explicit mapping table, admin-approved); SCIM 2.0 endpoint for Enterprise directory sync (create/suspend only; role changes stay in-app for SoD integrity); break-glass local admin per tenant exempt from SSO with mandatory MFA + alert-on-use.

## C5. IP Restriction (topic 127)
Per-tenant CIDR allowlists with scopes: whole-tenant | role-scoped (e.g., Finance only from office/VPN) | API-client-scoped. Evaluated at edge (WAF) + app (token claim vs source). Exceptions: time-boxed travel exceptions with approver + auto-expiry. All denials audited with geo context.

## C6. Session Management (topic 128)
Redis-backed session registry: sliding idle timeout (default 30 min web, configurable 15–120), absolute lifetime 12 h, concurrent session policy (allow | notify | single-session for Finance roles), device list UI with remote revoke, admin "revoke all tenant sessions" kill switch, refresh-token rotation with reuse detection (family revocation on theft signal). High-value actions (bank account change approval, DoA edits) always re-prompt credentials regardless of session age.

---

# PART D — PERMISSION FRAMEWORK (topic 06)

## D1. Model
**RBAC + scope + conditions (ABAC-lite).** Permission = `(capability, scope, conditions)`.
- **Capability registry** (~320 entries): `po.create`, `po.approve`, `po.override_feasibility`, `lc.apply`, `boe.assess.accept`, `vendor.bank.approve`, `costsheet.finalize`, `report.spend.view`, `admin.workflow.publish`…
- **Scope dimensions:** org subtree (node + descendants), company list, category tree, site list, amount ceiling, own-vs-all documents.
- **Conditions:** state guards (approve only in `PendingApproval`), SoD guards (cannot approve documents one created — enforced at engine level, not convention), maker-checker pairs (bank data: entry role ≠ approve role).

## D2. Role Architecture
Platform-defined **role templates** (18: Requester, Buyer, Procurement Head, Category Mgr, Sourcing Specialist, Contract Mgr, Import Executive, Import Mgr, Trade Finance Officer, Finance Mgr, CFO, Cost Accountant, AP Clerk, Store Keeper, QC Inspector, Auditor(read-all+audit), Compliance Officer, Tenant Admin) → tenants clone & customize; custom roles composed from capabilities; role versioning with change audit; **assignment = user × role × scope** (same role, different subtree per assignment).

## D3. Permission Matrix (excerpt; full matrix generated per tenant in Admin → Roles → Matrix Export)

| Capability | Req | Buyer | ProcHead | ImpExec | TFO | FinMgr | CFO | Auditor | Admin |
|---|---|---|---|---|---|---|---|---|---|
| pr.create | ✔ own | ✔ | ✔ | — | — | — | — | — | — |
| po.create | — | ✔ scope | ✔ | ✔ import | — | — | — | — | — |
| po.approve | — | — | ✔ ≤DoA | — | — | ✔ ≤DoA | ✔ | — | — |
| po.override_feasibility | — | — | — | — | — | — | ✔ +reason | — | — |
| lc.apply | — | — | — | ✔ | ✔ | — | — | — | — |
| lc.approve | — | — | — | — | — | ✔ ≤DoA | ✔ | — | — |
| vendor.bank.enter | — | ✔ | — | — | — | — | — | — | — |
| vendor.bank.approve | — | — | ✔ | — | — | ✔ | — | — | — |
| boe.assess.accept | — | — | — | ✔ ≤tol | — | — | ✔ | — | — |
| costsheet.finalize | — | — | — | — | — | ✔ | ✔ | — | — |
| audit.view_all | — | — | — | — | — | — | — | ✔ | ✔ |
| admin.doa.edit | — | — | — | — | — | — | — | — | ✔ +checker |

## D4. Evaluation & Performance
Login → resolve assignments → compile **permission set** (bitset over capability registry + scope predicates) → cache Redis (invalidate on assignment/role/org change) → BFF exposes to UI as declarative map (`<Can capability=... scope=...>`); server re-checks every command (UI hints are never authority). Decision P95 < 2 ms (in-memory bitset + memoized scope predicates).

## D5. Delegation & SoD
Time-boxed delegation (vacation): delegate receives capability subset with ceiling, all actions logged as "X on behalf of Y", auto-expiry, no re-delegation. SoD conflict matrix maintained per tenant (e.g., `vendor.bank.enter` × `invoice.approve` conflict) — assignment attempt warns; override requires Admin + Compliance dual approval and lands on the SoD exception report.

## D6. Edge Cases
Permission change mid-workflow (task re-evaluated at open; lost capability → task reassigned by rule); Auditor read-everything must still exclude field-encrypted values without explicit `audit.unmask` grant (dual-control unmask, alarmed); API clients get least-privilege scoped tokens with per-endpoint capability mapping; emergency access ("firecall") role activation requires two admins + auto-revokes in 4 h + full session recording flag.
