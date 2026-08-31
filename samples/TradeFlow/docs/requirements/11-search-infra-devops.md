# 11 — Search (OpenSearch), AWS Infrastructure, DevOps & Security

# A. Search — OpenSearch Design

## A.1 Topology & Isolation
- Amazon OpenSearch Service per cell: 3 data nodes (r6g.large.search to start) + 3 dedicated masters, Multi-AZ, EBS gp3.
- Indices monthly-rolled where time-heavy; **tenant isolation by filtered alias** (`docs-{tenant}` alias with `term: tenant_id`) + backend-role mapping; search-indexer signs requests via IAM, queries always carry tenant filter injected server-side (never from client).

## A.2 Indices & Searchable Fields
| Index | Documents | Key searchable fields |
|---|---|---|
| `procurement` | PR, PO, contracts, sourcing cases | doc no, vendor name, item names/desc (analyzed, bn+en analyzers), category, status, amount, dates, creator |
| `import` | files, shipments, BoE, transport docs, LC | file no, LC no, BL/AWB no, container no, BoE no, vessel, ports, HS, vendor, state, milestone dates |
| `supplier` | vendors + scorecards | name (edge-ngram for typeahead), trade names, country, categories, status, grade, risk, contacts |
| `documents` | vault OCR text | filename, doc type, extracted text, linked entity refs |
| `tasks` | workflow tasks | subject summary, assignee, sla, amount |
**Mappings:** keyword+text multi-fields; `icu_analyzer` + custom Bangla analyzer; numeric ranges for amounts; completion suggester on names; all docs carry `tenant_id, company_id, acl_scope` (filtered at query time for dept/own-scope roles).

## A.3 Query Features
- **Global omni-search** (Cmd+K): multi-index, type-grouped results, recent-entity boost (function score on updated_at), did-you-mean.
- **Procurement search:** facets (status, vendor, category, amount slabs, FY); saved filters.
- **Import search:** "find anything by any number" — BL, container, LC, BoE, file — exact-match boost on identifier fields with normalized analyzers (strip spaces/dashes).
- **Supplier search:** typeahead < 50 ms (edge-ngram), fuzzy fallback.
- Indexer: SQS consumer of domain events → upsert (idempotent on event id); nightly reconciliation sweep (compare counts/checksums vs. Postgres, re-index drift); full rebuild runbook via event archive.

# B. AWS Architecture

```
Route53 → CloudFront (Next.js static+ISR via S3 origin + /api/* origin to ALB)
        → WAF (managed rules + rate limits + geo rules)
ALB (public subnets) → ECS Fargate services (private subnets):
   web-bff (Next.js SSR)  api (ASP.NET)  worker  saga-manager  search-indexer
   intelligence-api (Python)  keycloak (2 tasks + RDS)        
Data layer: RDS PostgreSQL 16 Multi-AZ (+1 read replica) | ElastiCache Redis (cluster mode)
            OpenSearch Multi-AZ | S3 (documents, archive, exports) | KMS CMKs
Messaging: EventBridge bus | SQS queues+DLQs | EventBridge Scheduler | SES | SNS (SMS)
VPC: 3 AZ, public/private/data subnet tiers, VPC endpoints (S3, ECR, SecretsManager,
     CloudWatch, SQS, EventBridge) — no NAT for AWS traffic; single NAT GW for egress
```
- **Environments:** dev, staging, prod — separate accounts (AWS Organizations), prod cells `prod-cell-01..n`.
- **Scaling:** ECS target-tracking (CPU 60%, ALB req/target); worker scales on queue depth; RDS storage autoscaling; OpenSearch UltraWarm later for old indices.
- **Static/Media:** documents to S3 via presigned upload (size/type validated), CloudFront signed URLs for download, S3 lifecycle: IA at 90d, Glacier at 1y (except active retention class).

# C. Terraform Design
```
infra/
  modules/ network/ ecs-service/ rds/ redis/ opensearch/ eventing/ s3-docs/
          keycloak/ observability/ waf/ cicd/
  envs/ dev/ staging/ prod-cell-01/   (each: main.tf wiring modules, tfvars)
  global/ org, dns, ecr, identity-center
```
- State: S3 backend + DynamoDB lock per env; CI runs `fmt/validate/tflint/checkov` + `terraform plan` on PR (plan posted as comment), apply on tagged release with manual approval (prod).
- Conventions: every module outputs SSM parameters consumed by app deploy (no hardcoded ARNs); tagging standard (`tenant-cell`, `cost-center`, `service`) → AWS Cost Categories per cell for unit economics.
- Secrets: Secrets Manager (DB creds via RDS-managed rotation, Keycloak client secrets, SES keys); ECS task definitions reference secrets ARNs.

# D. Docker & ECS Deployment
- **API image:** `mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled` runtime, non-root, read-only FS, healthcheck `/healthz` (liveness) `/readyz` (readiness: DB+Redis+bus ping); multi-stage build with `dotnet publish -c Release /p:PublishReadyToRun=true`.
- **Web:** Next.js standalone output on `node:22-alpine` (distroless option), SSR via BFF.
- **Deploy:** ECS rolling for workers; **blue/green via CodeDeploy** for api/web (ALB test listener, 10%→100% with CloudWatch alarm gates: 5xx rate, latency, DLQ depth); circuit-break auto-rollback.
- DB migrations: one-off ECS task (same image, `--migrate`) gated before service shift; expand/contract discipline (doc 10 §6).

# E. CI/CD Pipeline (GitHub Actions)
```
PR:    restore→build→unit tests→ArchUnitNET boundary tests→integration tests
       (Testcontainers: Postgres+Redis+LocalStack)→RLS isolation test suite→
       dotnet format+analyzers→Trivy image scan→SAST (CodeQL)→tf plan
main:  build+push ECR (sha tag)→deploy dev→API contract tests (Schemathesis)→
       e2e (Playwright vs dev)→deploy staging (auto)→smoke+synthetic journeys
release tag: manual approval→migrate task→blue/green prod→post-deploy synthetics→
       notify; rollback = previous task set + migration contract safety
```
- Quality gates: coverage ≥ 80% domain layer, zero criticals (Trivy/CodeQL), seed-data drift check, OpenAPI diff (breaking-change blocker).
- **RLS isolation tests in CI are mandatory:** suite creates 2 tenants, runs every repository method under tenant A asserting zero leakage of B.

# F. Backup & Disaster Recovery
| Asset | Backup | DR |
|---|---|---|
| RDS | PITR 14d + daily snapshots 35d + monthly 7y; cross-region snapshot copy (ap-southeast-1) | Warm standby option Enterprise; default: restore-from-snapshot runbook, RTO 1h/RPO 5min in-region (Multi-AZ failover <60s) |
| S3 docs | Versioning + replication to DR region + Object Lock (compliance class for challans/BoE) | Region failover via replicated bucket |
| OpenSearch | Automated snapshots to S3 (hourly) | Restore runbook; search degrades gracefully (Postgres fallback queries) |
| Redis | AOF + daily snapshot | Cache — rebuildable |
| Event archive | S3 parquet, replicated | Source for projection rebuilds |
- Game days quarterly: AZ loss, DB failover, queue poison, region-restore tabletop. Runbooks in repo (`/runbooks`), each with verification checklist.

# G. Monitoring, Logging & Observability
- **OpenTelemetry end-to-end:** ASP.NET + Next.js + workers traced; ADOT collector sidecar → X-Ray (traces) + CloudWatch (metrics) + managed Grafana dashboards; correlation id = trace id propagated into outbox events and audit log.
- **Golden signals per service** + business SLIs: feasibility latency, outbox lag, projection lag, DLQ depth, duty-calc error rate, LC maturity-alert delivery success.
- Logs: structured JSON (Serilog), tenant_id+correlation enriched, CloudWatch Logs → subscription to OpenSearch (ops index) for ad-hoc; PII scrubbing processor.
- Alerting: CloudWatch alarms → SNS → PagerDuty/Slack; severity matrix; synthetic canaries (login, PO create, duty calc, search) every 5 min per cell.
- Cost observability: per-cell Cost Categories, anomaly detection, monthly unit-cost report (infra BDT/tenant).

# H. Security Architecture
- **Identity:** Keycloak hardened (admin on private ALB only, brute-force detection, token 15 min, PKCE, per-tenant IdP brokering); service auth via client-credentials with audience-scoped tokens.
- **AppSec:** OWASP ASVS L2 checklist in DoD; input validation (FluentValidation) + output encoding; SSRF-safe fetchers (webhook egress allow-list); file uploads AV-scanned (ClamAV Lambda) before vault admission; signed URL TTL ≤ 5 min.
- **Data:** TLS 1.3 everywhere (internal ALB TLS), KMS CMK per env + per-tenant data keys for bank-instruction blobs; field-level encryption for vendor bank accounts; secrets never in env vars (Secrets Manager refs).
- **Network:** zero inbound to private subnets except ALB SGs; SG-to-SG least privilege; VPC Flow Logs; GuardDuty + Security Hub + Inspector enabled; IMDSv2 enforced.
- **Tenant isolation defense-in-depth:** JWT → middleware tenant context → SQL `SET LOCAL` → RLS → S3 prefix IAM conditions → OpenSearch filtered alias → cache key prefixes — each layer independently tested.
- **SoD & fraud controls:** maker-checker (vendor bank, duty rates, FX), beneficiary-match hard rule at LC/TT, immutable audit, anomaly alerts (new beneficiary + high amount).
- Vulnerability mgmt: weekly dependency scan (Dependabot+Trivy), patch SLA (critical 72h), annual external pentest.

# I. Audit Trail & Compliance
- Append-only `platform.audit_log` (doc 10) + S3 Object Lock copies for legal docs; audit viewer UI (Auditor role): filter by entity/actor/date, before/after diff render.
- Compliance roadmap: SOC 2 Type I (mo 9) → Type II (mo 18); ISO 27001 alignment; BD compliance: NBR record retention (≥6y), VAT record formats (Mushak-support exports), Bangladesh Bank FX-reporting alignment (IMP matching data), data-protection readiness (consent + DSR runbooks for portal users).
- DPA + sub-processor register published; tenant-facing trust page (uptime, security whitepaper).
