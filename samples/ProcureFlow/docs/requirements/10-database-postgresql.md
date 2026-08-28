# 10 — Database Design (PostgreSQL 16)

## 1. Conventions
- Schemas per context: `platform, vendor, sourcing, proc, import, tradefin, customs, costing, inv, fin, mart`.
- Every tenant table: `tenant_id uuid NOT NULL`, `company_id uuid` where company-scoped; standard columns `created_at timestamptz DEFAULT now(), created_by uuid, updated_at, updated_by, row_version int` (optimistic via `xmin` exposure or explicit version).
- Money: `numeric(18,4)` + `currency char(3)`; quantities `numeric(18,6)` + `uom`; all timestamps `timestamptz` (UTC); business dates `date`.
- Soft delete only on masters (`archived_at`); transactional docs use status, never delete.
- RLS enabled on every tenant table via the template policy (doc 02 §2.2). Composite PKs include `tenant_id` on partitioned tables.

## 2. Representative DDL (core spine — full catalog follows the same pattern)

```sql
-- ============ platform ============
CREATE TABLE platform.tenant (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  slug text UNIQUE NOT NULL, name text NOT NULL, plan text NOT NULL,
  base_currency char(3) NOT NULL DEFAULT 'BDT',
  fiscal_year_start smallint NOT NULL DEFAULT 7,        -- July (BD)
  settings jsonb NOT NULL DEFAULT '{}', status text NOT NULL DEFAULT 'active',
  created_at timestamptz NOT NULL DEFAULT now());

CREATE TABLE platform.company (
  id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL REFERENCES platform.tenant(id),
  code text NOT NULL, legal_name text NOT NULL,
  bin text, irc_no text, irc_ceiling numeric(18,2), tin text, vat_reg text,
  address jsonb, UNIQUE (tenant_id, code));

CREATE TABLE platform.outbox (
  id bigint GENERATED ALWAYS AS IDENTITY,
  tenant_id uuid NOT NULL, event_id uuid NOT NULL DEFAULT gen_random_uuid(),
  aggregate_type text NOT NULL, aggregate_id uuid NOT NULL,
  event_type text NOT NULL, event_version smallint NOT NULL DEFAULT 1,
  payload jsonb NOT NULL, correlation_id uuid, causation_id uuid,
  occurred_at timestamptz NOT NULL DEFAULT now(),
  dispatched_at timestamptz, PRIMARY KEY (id))
  PARTITION BY RANGE (occurred_at);                      -- monthly; drop after archive

CREATE TABLE platform.audit_log (
  id bigint GENERATED ALWAYS AS IDENTITY,
  tenant_id uuid NOT NULL, actor_id uuid, actor_ip inet,
  action text NOT NULL, entity_type text NOT NULL, entity_id uuid,
  before jsonb, after jsonb, correlation_id uuid,
  at timestamptz NOT NULL DEFAULT now(), PRIMARY KEY (id, at))
  PARTITION BY RANGE (at);                               -- monthly, 10y retention to S3

CREATE TABLE platform.workflow_instance (
  id uuid PRIMARY KEY, tenant_id uuid NOT NULL,
  definition_key text NOT NULL, definition_version int NOT NULL,
  subject_type text NOT NULL, subject_id uuid NOT NULL,
  state text NOT NULL, context jsonb NOT NULL,
  started_at timestamptz NOT NULL DEFAULT now(), completed_at timestamptz);
CREATE TABLE platform.workflow_task (
  id uuid PRIMARY KEY, tenant_id uuid NOT NULL,
  instance_id uuid NOT NULL REFERENCES platform.workflow_instance(id),
  step_id text NOT NULL, assignee_position uuid, assignee_user uuid,
  status text NOT NULL DEFAULT 'open', sla_due_at timestamptz,
  decided_by uuid, decision text, comment text, decided_at timestamptz);

-- ============ proc ============
CREATE TABLE proc.purchase_order (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL, company_id uuid NOT NULL,
  po_no text NOT NULL, revision smallint NOT NULL DEFAULT 0,
  vendor_id uuid NOT NULL, type text NOT NULL,            -- domestic|import|service
  currency char(3) NOT NULL, incoterm text, payment_mode text,
  status text NOT NULL DEFAULT 'draft',
  total_amount numeric(18,4) NOT NULL DEFAULT 0,
  feasibility_score smallint, feasibility_snapshot jsonb,
  contract_id uuid, import_file_id uuid,
  created_at timestamptz NOT NULL DEFAULT now(), created_by uuid NOT NULL,
  PRIMARY KEY (tenant_id, id), UNIQUE (tenant_id, company_id, po_no, revision))
  PARTITION BY HASH (tenant_id);                          -- 16 partitions
CREATE TABLE proc.po_line (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL, po_id uuid NOT NULL,
  line_no smallint NOT NULL, item_id uuid, description text NOT NULL,
  hs_code char(8), qty numeric(18,6) NOT NULL, uom text NOT NULL,
  unit_price numeric(18,4) NOT NULL, need_by date,
  received_qty numeric(18,6) NOT NULL DEFAULT 0,
  invoiced_qty numeric(18,6) NOT NULL DEFAULT 0,
  budget_line_id uuid, cost_center_id uuid,
  PRIMARY KEY (tenant_id, id),
  FOREIGN KEY (tenant_id, po_id) REFERENCES proc.purchase_order(tenant_id, id))
  PARTITION BY HASH (tenant_id);

-- ============ import ============
CREATE TABLE import.import_file (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL, company_id uuid NOT NULL,
  file_no text NOT NULL, state text NOT NULL DEFAULT 'planned',
  vendor_id uuid NOT NULL, origin_country char(2), mode text,
  pol text, pod text, payment_instrument text,             -- lc|tt|contract
  cnf_agent_id uuid, opened_at timestamptz NOT NULL DEFAULT now(),
  closed_at timestamptz,
  PRIMARY KEY (tenant_id, id), UNIQUE (tenant_id, company_id, file_no))
  PARTITION BY HASH (tenant_id);

CREATE TABLE import.cost_element (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL, file_id uuid NOT NULL, sheet_id uuid,
  element_type text NOT NULL, stage text NOT NULL,         -- estimated|accrued|actual
  scope text NOT NULL DEFAULT 'file', scope_ref uuid,
  driver text NOT NULL, currency char(3) NOT NULL,
  amount numeric(18,4) NOT NULL, fx_rate numeric(18,8) NOT NULL,
  amount_base numeric(18,4) GENERATED ALWAYS AS (amount * fx_rate) STORED,
  allocatable boolean NOT NULL DEFAULT true,
  source_doc_type text, source_doc_id uuid,
  PRIMARY KEY (tenant_id, id)) PARTITION BY HASH (tenant_id);

-- ============ customs ============
CREATE TABLE customs.hs_code (
  code char(8) PRIMARY KEY, chapter char(2) NOT NULL,
  description text NOT NULL, uom text, status text NOT NULL DEFAULT 'active');
CREATE TABLE customs.duty_rate (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  hs_code char(8) NOT NULL REFERENCES customs.hs_code(code),
  component text NOT NULL,                                  -- CD|RD|SD|VAT|AIT|AT
  rate numeric(8,4) NOT NULL, specific_rate numeric(18,4), specific_uom text,
  tariff_value numeric(18,4),
  effective_from date NOT NULL, effective_to date,
  source text NOT NULL, source_ref text,
  approved_by uuid, approved_at timestamptz,
  EXCLUDE USING gist (hs_code WITH =, component WITH =,
    daterange(effective_from, effective_to, '[]') WITH &&));  -- no overlapping periods

CREATE TABLE customs.bill_of_entry (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL, company_id uuid NOT NULL, file_id uuid NOT NULL,
  boe_no text NOT NULL, boe_date date NOT NULL, customs_office text NOT NULL,
  cnf_agent_id uuid, lane char(1), status text NOT NULL DEFAULT 'submitted',
  customs_fx_rate numeric(18,8) NOT NULL,
  total_av numeric(18,4), duty_totals jsonb,                 -- {CD:..,RD:..,...}
  PRIMARY KEY (tenant_id, id),
  UNIQUE (tenant_id, company_id, boe_no, boe_date)) PARTITION BY HASH (tenant_id);
CREATE TABLE customs.boe_line (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL, boe_id uuid NOT NULL, ci_line_id uuid,
  hs_declared char(8) NOT NULL, qty numeric(18,6), uom text,
  cif_fcy numeric(18,4) NOT NULL, av numeric(18,4) NOT NULL,
  cd numeric(18,4) DEFAULT 0, rd numeric(18,4) DEFAULT 0,
  sd numeric(18,4) DEFAULT 0, vat numeric(18,4) DEFAULT 0,
  ait numeric(18,4) DEFAULT 0, at numeric(18,4) DEFAULT 0,
  rate_lineage jsonb,                                        -- duty_rate ids used
  assessed boolean NOT NULL DEFAULT false,
  PRIMARY KEY (tenant_id, id)) PARTITION BY HASH (tenant_id);

-- ============ tradefin ============
CREATE TABLE tradefin.letter_of_credit (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL, company_id uuid NOT NULL, file_id uuid NOT NULL,
  lc_no text, bank_id uuid NOT NULL, facility_id uuid,
  lc_type text NOT NULL, tenor_days int NOT NULL DEFAULT 0,
  currency char(3) NOT NULL, amount numeric(18,4) NOT NULL,
  tolerance_pct numeric(5,2) NOT NULL DEFAULT 0,
  issue_date date, expiry_date date, latest_shipment date,
  margin_pct numeric(5,2), margin_blocked numeric(18,4) NOT NULL DEFAULT 0,
  status text NOT NULL DEFAULT 'applied',
  PRIMARY KEY (tenant_id, id), UNIQUE (tenant_id, company_id, lc_no))
  PARTITION BY HASH (tenant_id);

-- ============ costing ============
CREATE TABLE costing.line_landed_cost (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  tenant_id uuid NOT NULL, sheet_id uuid NOT NULL, file_id uuid NOT NULL,
  ci_line_id uuid NOT NULL, item_id uuid, received_qty numeric(18,6) NOT NULL,
  goods_value_base numeric(18,4) NOT NULL,
  duty_cost numeric(18,4) NOT NULL, logistics_cost numeric(18,4) NOT NULL,
  finance_cost numeric(18,4) NOT NULL, other_cost numeric(18,4) NOT NULL,
  unit_landed_cost numeric(18,6) NOT NULL,
  PRIMARY KEY (tenant_id, id)) PARTITION BY HASH (tenant_id);

-- ============ inv (high volume, time-relevant) ============
CREATE TABLE inv.inventory_value_ledger (
  id bigint GENERATED ALWAYS AS IDENTITY,
  tenant_id uuid NOT NULL, company_id uuid NOT NULL,
  item_id uuid NOT NULL, site_id uuid NOT NULL,
  txn_type text NOT NULL, qty numeric(18,6) NOT NULL,
  unit_cost numeric(18,6) NOT NULL, value_delta numeric(18,4) NOT NULL,
  source_doc_type text, source_doc_id uuid,
  posted_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (id, posted_at))
  PARTITION BY RANGE (posted_at);                            -- monthly
```

## 3. Partition Strategy
| Table family | Method | Rationale |
|---|---|---|
| Transactional docs (PO, file, BoE, LC, GRN, invoices, cost tables) | HASH(tenant_id), 16 partitions/cell | Even spread, partition-wise joins on tenant, no hot tail |
| Append-only time series (audit_log, outbox, value ledger, budget_txn, notification_log, swift_message, shipment_milestone) | RANGE monthly on time | Cheap retention (detach→archive to S3 parquet→drop), vacuum locality |
| mart.* facts | RANGE monthly on business date | Reporting pruning |
Automation: `pg_partman` for create-ahead + retention; partition-creation drill in CI. Future giant tenants → move tenant to dedicated cell (logical replication copy, cutover via control plane).

## 4. Indexing Strategy
- Default per doc table: `(tenant_id, status)`, `(tenant_id, created_at DESC)`, FK columns; unique business keys as in DDL.
- Targeted: `po_line (tenant_id, item_id, created_at DESC)` for last-price lookups; `duty_rate (hs_code, component) INCLUDE (rate, specific_rate)` + the GiST exclusion doubles as range lookup; `payment_obligation (tenant_id, due_date) WHERE status='open'` partial; `workflow_task (tenant_id, assignee_user, status) WHERE status='open'`; trigram GIN on vendor/item names for typeahead (`pg_trgm`); `cost_element (tenant_id, file_id, element_type, stage)`; BRIN on big time-range tables (`posted_at`).
- JSONB: expression GIN only where queried (e.g., `feasibility_snapshot->'score'` not needed — extracted column instead; rule: hot fields = real columns, jsonb = cold detail).
- Discipline: `pg_stat_statements` + auto-explain in staging; index review per release; no index added without a named query.

## 5. Reporting Database Strategy
- **Tier 1 (live ops reports):** RDS read replica; mart schema views with RLS mirrored; Dapper read DAOs pinned to replica connection string.
- **Tier 2 (analytics marts):** `mart.fact_landed_cost`, `mart.fact_spend`, `mart.fact_clearance_cycle`, `mart.fact_lc_exposure`, `mart.dim_*` — maintained by event-driven incremental upserts (projection workers) + nightly reconciliation job (recompute from source, diff-alert). Heavy aggregates as materialized views refreshed concurrently on schedule.
- **Tier 3 (warehouse, Enterprise):** S3 parquet event/state archive queried via Athena; optional tenant-facing data share (per-tenant S3 export). Keeps OLTP clean; no Redshift until cross-cell analytics demands it.
- FX-normalized series: facts store both txn currency and base amounts + monthly average-rate table for normalized trend views.

## 6. Data Lifecycle & Integrity
- PITR (WAL) 14 days; daily snapshots 35 days; monthly archival snapshots 7 years (NBR ≥ 6y).
- Constraint philosophy: DB enforces tenancy, uniqueness, periods non-overlap, FK; domain invariants in code; CHECKs for enums/state values.
- Migration tooling: DbUp-style ordered SQL migrations in repo, applied by deploy job with advisory lock; expand-migrate-contract pattern for zero-downtime.
- Tenant export: per-schema COPY of tenant rows + S3 document manifest, packaged; tenant delete = RLS-scoped cascade job + document purge with certificate.
