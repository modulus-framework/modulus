# 02 — SRS, Multi-Tenant Design, Roles, Permissions & Workflow Engine

## 1. Software Requirements Specification

### 1.1 Functional Requirement Index (FR)
| ID | Requirement | Module Doc |
|---|---|---|
| FR-100s | Vendor lifecycle (registration → qualification → evaluation → scorecard) | 03 |
| FR-200s | Requisition, RFQ/RFP, bid analysis, PO, contract | 03 |
| FR-300s | Import file, documents (PI/CI/PL/BL/AWB/COO), shipment & container tracking | 04 |
| FR-400s | LC, B2B LC, TT, loans, margin, payment schedules | 05 |
| FR-500s | Duty cascade computation, assessment, payment tracking, rate history | 06 |
| FR-600s | Landed cost allocation & calculation, cost sheets | 06 |
| FR-700s | Feasibility, forecasting, risk engines | 07 |
| FR-800s | GRN, batch/lot/serial, valuation, AP/GL integration | 08 |
| FR-900s | Reporting & dashboards | 08 |
| FR-1000s | Platform: tenancy, RBAC, workflow, audit, notifications, search | this doc |

### 1.2 Non-Functional Requirements
| Category | Requirement |
|---|---|
| Availability | 99.9% monthly; RTO 1 h, RPO 5 min |
| Performance | P95 read < 200 ms, write < 400 ms; duty calc < 100 ms; feasibility score < 3 s; reports < 10 s or async |
| Scale | 500 tenants, 20k users, 1M PO lines/yr, 200k import files/yr per cell; 10 y retention |
| Security | OWASP ASVS L2, encryption in transit (TLS 1.3) & at rest (KMS), tenant isolation tested in CI |
| Auditability | Append-only audit log for all state changes incl. actor, before/after, IP, correlation id |
| Usability | WCAG 2.1 AA, Bangla/English, responsive ≥ 768 px full UX, mobile read+approve |
| Integrability | REST + webhooks; OpenAPI 3.1; idempotency keys on all writes |
| Data | Daily logical backups + PITR; tenant export (full data, on request, ≤ 72 h) |

## 2. Multi-Tenant SaaS Design

### 2.1 Tenancy Model
- **Pool model:** shared PostgreSQL, shared schema, every tenant-scoped table carries `tenant_id UUID NOT NULL` with **Row-Level Security** enforced policies (defense-in-depth beneath application filters).
- **Hierarchy within tenant:** `tenant → company (legal entity, BIN/IRC holder) → business_unit → site (factory/warehouse/office)`. All transactional records carry `company_id`; documents like LC and Bill of Entry are company-scoped (legal identity matters).
- **Cells:** a "cell" = one RDS cluster + app services. Tenants are assigned to cells; large/regulated tenants can get dedicated cells. A global control-plane DB maps `tenant → cell`, plans, feature flags, and billing.

### 2.2 Isolation Mechanics
```sql
-- every request sets:
SET LOCAL app.tenant_id = '7d9f...';
-- policy template:
ALTER TABLE purchase_order ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON purchase_order
  USING (tenant_id = current_setting('app.tenant_id')::uuid);
```
- App connects as `app_user` (no BYPASSRLS). Migrations run as separate role.
- Dapper layer: a `TenantConnectionFactory` opens the connection, begins the transaction, executes `SET LOCAL`, and hands the connection to handlers — tenant context comes only from the validated JWT, never from request payloads.
- S3 keys prefixed `tenant/{tenant_id}/...` with IAM policy conditions; OpenSearch indices per-tenant-alias with filtered aliases; Redis keys prefixed; per-tenant KMS data keys (envelope) for highly sensitive blobs (bank instructions).

### 2.3 Tenant Configurability
Per tenant: fiscal calendar, base currency, duty structure overrides & SRO registry, approval matrices, numbering schemes (`PO-{COMPANY}-{FY}-{SEQ}`), match tolerances, COA mapping, notification channels, feature flags (plan-driven), UI language default.

### 2.4 Identity — Keycloak
- Single realm `TradeFlow` (operationally simpler than realm-per-tenant at 500 tenants); tenants modeled as Keycloak **organizations/groups**; JWT carries `tenant_id`, `company_ids`, `roles`, `plan`.
- OIDC code flow + PKCE for Next.js (BFF pattern: tokens stay server-side, session cookie to browser). Service-to-service via client-credentials.
- Enterprise SSO: per-tenant IdP brokering (SAML/OIDC) mapped into the tenant's group. SCIM provisioning for Enterprise plan. MFA (TOTP) enforceable per tenant. Token lifetime 15 min access / rotating refresh.

## 3. Organizational Structure (in-app)

```
Tenant (Group)
 └─ Company (BIN, IRC, ERC, TIN, VAT reg.)        e.g., "ABC Textiles Ltd."
     └─ Business Unit (division/profit center)     e.g., "Spinning", "Trading"
         └─ Site (factory/warehouse/HO)            e.g., "Gazipur Unit-2"
             └─ Department (cost center link)      e.g., "Maintenance", "Dyeing"
                 └─ Position → User assignment
```
- Positions (e.g., "GM Procurement – Spinning") are the anchors for approval routing and delegation — users move, positions persist.
- Cost centers map to department×site; budget ownership follows this tree.
- A user may hold multiple positions across companies (common in BD groups); the UI carries an active-company switcher; every action is recorded against the active company context.

## 4. User Roles (built-in; tenants may clone & customize)

| Role | Summary |
|---|---|
| Tenant Owner | Billing, plan, tenant settings, user admin |
| System Admin | Masters, numbering, workflow config, integrations |
| Procurement Head | Full procurement, approval authority, vendor decisions |
| Procurement Officer / Buyer | PR/RFQ/PO operations within DoA |
| Sourcing Manager | RFQ/RFP, bid analysis, awards |
| Vendor Manager | Vendor lifecycle, qualification, scorecards |
| Import/Commercial Manager | Import files, LC, shipping docs, C&F coordination |
| Commercial Officer | Document entry, milestone updates |
| Finance Head / CFO | Budgets, LC approvals, cost sheet finalization, GL |
| AP Accountant | Invoice capture, matching, payment runs |
| Tax/VAT Officer | Duty structures, assessments, challans, AIT/AT ledger |
| Store/Warehouse Manager | GRN, putaway, batches |
| QC Inspector | Quality acceptance on receipts |
| Budget Owner (Dept Head) | PR approval, budget visibility for own cost centers |
| Auditor (read-only) | Everything read-only + audit log |
| Supplier User (portal) | Own RFQs, bids, POs, ASN, invoices |
| C&F Agent (portal) | Assigned files: documents, assessment status, charge bills |
| Executive Viewer | Dashboards & reports only |

## 5. Permission Matrix (capability codes: C create, R read, U update, A approve, X execute/special; scope: T tenant, Co company, BU, Dept, Own)

| Capability \ Role | ProcHead | Buyer | Sourcing | VendorMgr | ImportMgr | FinHead | AP | Tax | Store | QC | BudgetOwner | Auditor | Supplier | C&F |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Vendor master | A | R | R | CRUA | R | R | R | R | – | – | R | R | Own-U | – |
| Vendor bank a/c (maker-checker) | A | – | – | C/U | – | A | R | – | – | – | – | R | Own-C | – |
| PR | A(T) | CRU(BU) | R | – | R | R | – | – | R | – | A(Dept) | R | – | – |
| RFQ/RFP | A | C R U | CRUX | R | R | R | – | – | – | – | – | R | Own bids | – |
| Bid analysis & award | A | R | CRUX | R | – | R | – | – | – | – | – | R | – | – |
| PO | A(T) | CRU | R | – | CRU(import) | A(>limit) | R | R | R | – | R(Dept) | R | Own-R | – |
| Feasibility override | A | – | – | – | – | A | – | – | – | – | – | R | – | – |
| Contract | A | R | CRU | R | R | A | R | – | – | – | – | R | Own-R | – |
| Import file/docs | R | R | – | – | CRUX | R | R | R | – | – | – | R | Own-R | Assigned-U |
| LC / TT / loans | R | – | – | – | CRU | A | R | R | – | – | – | R | – | – |
| Duty structure & rates | – | – | – | – | R | A | – | CRUA | – | – | – | R | – | – |
| Assessment & duty payment | – | – | – | – | U | R | – | CRUX | – | – | – | R | – | Assigned-U |
| Landed cost sheet | R | R | – | – | CRU | A(final) | R | R | – | – | – | R | – | – |
| GRN | R | R | – | – | R | R | R | – | CRUX | A(QC) | – | R | ASN-C | – |
| Invoice & matching | R | R | – | – | R | A(exc.) | CRUX | R | R | – | – | R | Own-C | Charge-C |
| Budget setup | R | – | – | – | – | CRUA | R | – | – | – | R(own) | R | – | – |
| Reports/dashboards | T | BU | T | T | T | T | Co | Co | Site | Site | Dept | T | Own | Assigned |
| Workflow/config admin | – | – | – | – | – | – | – | – | – | – | – | R | – | – |
| Audit log | – | – | – | – | – | R | – | – | – | – | – | R | – | – |

(System Admin: all config CRUA, no transactional approvals — enforced segregation. Tenant Owner: admin + billing.)
**Enforcement:** permissions resolved at login into a compact policy set (role × capability × scope), cached in Redis, evaluated by an ASP.NET authorization handler; row scope applied in SQL predicates (`company_id IN @allowed`, dept scope for budget owners). Segregation-of-duties rules: creator ≠ approver, vendor-bank maker ≠ checker, duty-rate maker ≠ approver — validated by the workflow engine.

## 6. Approval Hierarchy & Delegation of Authority (DoA)

### 6.1 DoA Matrix (tenant-configurable; defaults)
| Document | Amount (BDT) | Chain (positions) |
|---|---|---|
| PR | ≤ 1L | Dept Head |
| PR | 1L–10L | Dept Head → Procurement Head |
| PR | > 10L | Dept Head → Procurement Head → CFO |
| PO (domestic) | ≤ 5L | Procurement Head |
| PO (domestic) | > 5L | Procurement Head → CFO |
| PO (import) | any | Procurement Head → Feasibility Gate → CFO; > 1 Cr adds MD |
| RFQ award | > 5L | Sourcing Mgr → Procurement Head (+CFO if lowest bid not chosen) |
| LC application | any | Import Mgr → CFO |
| LC amendment | value-increasing | CFO; else Import Mgr |
| Duty rate change | any | Tax Officer (maker) → Finance Head (checker) |
| Landed cost finalization | any | Import Mgr → Finance Head |
| Invoice exception | > tolerance | AP → Finance Head |
| Budget revision | any | Budget Owner → CFO |
| Vendor qualification | strategic category | Vendor Mgr → Procurement Head |

### 6.2 Rules
- Routing targets **positions**; resolution to users happens at task creation (re-resolved on reassignment).
- **Delegation:** time-boxed (vacation) with full audit; delegate's approvals stamped "on behalf of".
- **Escalation:** SLA per step (default 48 h) → reminder at 50%, escalate to supervisor position at breach; configurable auto-approve only for non-financial steps.
- **Parallel + quorum** supported (e.g., technical + commercial evaluation in parallel; 2-of-3 committee quorum).
- Amount slabs evaluated on **base-currency equivalent** at dated rate; re-approval required if amount grows > 5% post-approval.

## 7. Workflow Engine Design

### 7.1 Model
A tenant-configurable, definition-driven state machine + human-task engine. Definitions are versioned JSON; instances bind to an aggregate (`subject_type`, `subject_id`).

```jsonc
{
  "key": "po-approval", "version": 7,
  "context": ["amountBDT", "isImport", "feasibilityScore", "categoryCode"],
  "steps": [
    { "id": "dept",      "type": "approval", "assignee": {"position": "DEPT_HEAD", "scope": "requesterDept"}, "slaHours": 48 },
    { "id": "feas",      "type": "gate",     "when": "isImport",
      "condition": "feasibilityScore >= tenant.minFeasibilityScore",
      "onFail": { "route": "override", "assignee": {"position": "CFO"}, "requireReason": true } },
    { "id": "prochead",  "type": "approval", "assignee": {"position": "PROC_HEAD"} },
    { "id": "cfo",       "type": "approval", "when": "amountBDT > 500000",
      "assignee": {"position": "CFO"}, "slaHours": 24, "escalateTo": {"position": "MD"} },
    { "id": "dispatch",  "type": "system",   "action": "po.dispatch" }
  ]
}
```

### 7.2 Components
- **Definition service:** CRUD + versioning; in-flight instances pin to their version; graphical designer (Enterprise) emits the same JSON.
- **Instance engine:** MediatR command `AdvanceWorkflow` processes transitions inside the aggregate's transaction; emits `workflow.step.completed`, `workflow.completed`, `workflow.rejected` via outbox.
- **Task inbox:** unified "My Approvals" across modules (OpenSearch-backed list: subject summary, amount, age, SLA color). Bulk approve where policy allows. Email/in-app/WhatsApp(Business API, Enterprise) actions deep-link; email approve uses signed one-time action tokens.
- **Condition language:** restricted expression evaluator (no arbitrary code) over declared context variables; validated at definition save.
- **Audit:** every task action stores actor, decision, comment, attachments, timestamp, IP; rejection returns subject to Draft with mandatory reason.
- **Recovery:** stuck-instance detector (no transition > SLA×3) surfaces to admin console; compensating actions documented per definition (e.g., release budget reservation on rejection).

### 7.3 Workflow ↔ Saga split
Human approval chains = workflow engine (interactive, tenant-configured). Long-running cross-context automation (import file orchestration, landed-cost finalization) = sagas (doc 09). Workflows may signal sagas via domain events and vice versa.

## 8. Notifications Framework
Channels: in-app (web push), email (SES), SMS (local aggregator), webhook. Tenant-level routing matrix (event × role × channel). Digest mode per user. Critical events (LC maturity T-7/T-3/T-1, demurrage 70%, SLA breach, assessment variance) are non-suppressible for responsible positions. Template engine with Bangla variants; all sends logged with delivery status.
