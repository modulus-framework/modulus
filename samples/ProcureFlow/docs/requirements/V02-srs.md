# V02 — Software Requirement Specification (SRS)
Covers master topic: 02

---

## 1. System Overview
Modular-monolith .NET 9 application (microservice-ready seams at bounded-context boundaries), PostgreSQL 16 system of record, Redis cache/session, OpenSearch search/analytics indexes, S3 document store, EventBridge+SQS event backbone, Keycloak identity, Next.js web + PWA mobile, Python FastAPI ML sidecars. Deployed on AWS ECS Fargate (Kubernetes-portable), IaC via OpenTofu.

## 2. Functional Requirement Index
FRs are owned by volumes; the SRS assigns IDs and acceptance style. Numbering: `FR-<ctx>-<nnn>` where ctx ∈ {PLT, ORG, IDM, PRM, WFL, NTF, VEN, SRC, CNT, PRC, GRN, INV, BUD, SPD, IMP, SHP, CUS, TF, CST, WMS, FIN, AI, RPT, SRCH, API, MOB, DMS, AUD, SEC, LOC, MDM}. Every FR ships with: actor, trigger, main flow, alternate flows, validation set reference, events emitted, permission required, audit expectation. Volumes V03–V25 enumerate them; V02 governs the format. Baseline count target: ≈ 1,400 FRs.

## 3. Non-Functional Requirements

### 3.1 Performance
| ID | Requirement |
|---|---|
| NFR-P-01 | P95 read API < 200 ms, write < 400 ms at 500 concurrent users/cell |
| NFR-P-02 | Feasibility engine full score < 3 s; duty calc < 300 ms |
| NFR-P-03 | OCR extraction pipeline P95 < 45 s/document (async, user-notified) |
| NFR-P-04 | Search-as-you-type < 150 ms; omni-search full query < 500 ms |
| NFR-P-05 | Canned reports < 5 s from marts; builder queries capped 60 s with async fallback |
| NFR-P-06 | Bulk import: 10k PO lines ≤ 5 min with progress streaming |

### 3.2 Availability & Recovery
99.9% monthly (Enterprise SLA), Multi-AZ everything; RTO 1 h, RPO 5 min; DR region warm standby for data (cross-region replicas + S3 CRR), infra re-creatable ≤ 4 h via IaC; brown-out mode: search/AI degrade before core transactions.

### 3.3 Scalability
Design point: 1,000 tenants/cell, 50M document rows/tenant-year max, 2,000 events/s platform-wide; horizontal service scaling; partitioned tables (V26); cell-based sharding with tenant→cell routing at edge; no cross-cell transactions.

### 3.4 Security (summary; full V24)
Zero-trust: mTLS service mesh, OIDC everywhere, RLS as last-line tenant isolation, field-level encryption (bank accounts, salaries in budgets), KMS per-tenant DEK envelope, OWASP ASVS L2 target, quarterly pentest, immutable audit (append-only + S3 Object Lock), maker-checker on money-adjacent master data.

### 3.5 Compliance
SOC 2 Type I (mo 10) → Type II (mo 20); NBR record retention ≥ 6 years; Bangladesh Bank FX regulation alignment (IMP evidence chain); data residency: BD tenants' data in-region where AWS presence allows, else Singapore with contractual disclosure; GDPR-grade DSR tooling for regional expansion.

### 3.6 Usability & Accessibility
WCAG 2.1 AA; bilingual en/bn with Bangla numeral option; BDT Lakh/Crore formatting; every computed figure explainable in ≤ 2 clicks; keyboard-complete approval flows; mobile PWA for approvals/receiving offline-tolerant (V23).

### 3.7 Auditability
Every state change: who/when/what-diff/why(reason codes)/correlation-id; workflow decisions store input snapshot; AI decisions store model version + feature vector hash; audit queryable per aggregate and exportable per auditor request.

### 3.8 Data Quality
Duplicate detection SLAs (vendor ≥ 92% precision, invoice ≥ 97%); mandatory reference-data governance (HS codes, UOM, currencies via MDM V25); all documents carry source (`manual|ocr|api|import`) and confidence where AI-extracted.

## 4. Interfaces
- **API platform (V22):** REST (OpenAPI 3.1), webhooks with HMAC signing + retry, bulk endpoints (NDJSON), rate limits per plan; GraphQL read gateway for report builder (internal).
- **File interchange:** supplier catalogs (CSV/XLSX), bank statements (MT940/CSV), tariff updates (platform ops), migration templates (V28).
- **Email/SMS/WhatsApp/Push:** SES, BD SMS aggregators (multi-provider failover), WhatsApp Business Cloud API templates, FCM/APNs.
- **Future integrations:** ASYCUDA (when public API), Bangladesh Bank dashboards, bKash/Nagad disbursement (AP execution, post-MVP), vessel tracking (AIS providers), Stripe/SSLCommerz billing.

## 5. Constraints
Single-region primary per cell; PostgreSQL as only OLTP store (no polyglot persistence in v2); LLM calls only via internal AI gateway (prompt/PII policy enforced, tenant data never used for cross-tenant training without opt-in); no direct DB access for tenants (API/exports only).

## 6. Acceptance & Traceability
Requirements→design→test traceability maintained in-repo (`/docs/traceability.csv` generated in CI); each FR must map to ≥ 1 automated test or explicit manual test case; NFRs verified by k6 load suites + chaos drills (V28).
