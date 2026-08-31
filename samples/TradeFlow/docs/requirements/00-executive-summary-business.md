# 00 — Executive Summary & Business Strategy

## 1. Executive Summary

### 1.1 Vision
TradeFlow is an enterprise-grade, multi-tenant SaaS platform unifying **Source-to-Pay procurement, vendor management, import management, trade finance, customs/duty tracking, and landed cost intelligence** in a single system — purpose-built for import-dependent economies, with Bangladesh as the launch market.

### 1.2 The Problem
Import-heavy enterprises in Bangladesh (RMG, textile, pharma, food, construction, trading) run procurement on a fragmented stack: Excel for landed cost, Tally/ERP for accounting, email/WhatsApp for vendor negotiation, paper files for LC and customs documents. Consequences:

- **No pre-purchase visibility of true landed cost.** Duty cascades (CD → RD → SD → VAT → AIT → AT) on assessable value are computed manually, often wrongly, after goods arrive.
- **No feasibility analysis before PO.** Buyers commit to imports without knowing expected margin, timeline, or supplier risk.
- **LC lifecycle is opaque.** Margins blocked at banks, amendment costs, maturity dates, and back-to-back LC exposure are tracked in spreadsheets.
- **Customs leakage.** Wrong HS classification, missed SRO benefits, and inability to challenge assessments cost 2–8% of import value.
- **No spend intelligence.** Procurement decisions are relationship-driven, not data-driven.

### 1.3 The Solution
A single platform where a tenant can: register and score vendors → raise requisitions → run RFQ/RFP with bid analysis → get an **automated purchase feasibility score** (predicted landed cost, margin, timeline, supplier risk) **before approving the PO** → manage the import lifecycle (PI → LC/TT → shipment → customs → port clearance) → capture every cost element → allocate to items via the **Landed Cost Engine** → push true unit cost into inventory valuation and GL.

### 1.4 Differentiators
1. **Bangladesh-native duty engine** — full NBR duty cascade, SRO/exemption awareness, HS-code-level rate history, AIT/AT treatment as advance taxes vs. cost.
2. **Pre-PO feasibility scoring** — the only system in the market that answers "should we import this, from this supplier, now?" with a quantified score.
3. **Landed cost as a first-class engine**, not a journal afterthought — multi-driver allocation (value/weight/volume/qty/duty), shipment-level and item-level, with variance vs. forecast.
4. **Trade finance depth** — LC, back-to-back LC, margin tracking, import loans (LTR/MPI/MIB), SWIFT reference chains.
5. **True multi-tenant SaaS** — PostgreSQL RLS isolation, tenant-level configurability of duty structures, approval chains, and chart-of-account mappings.

### 1.5 Platform Summary
| Dimension | Decision |
|---|---|
| Backend | .NET 9, ASP.NET Core, DDD + CQRS (MediatR), Dapper, PostgreSQL 16 |
| Frontend | Next.js 14 (App Router), TypeScript, Tailwind CSS, shadcn/ui |
| Infra | AWS — ECS Fargate, RDS PostgreSQL Multi-AZ, ElastiCache Redis, OpenSearch, S3, CloudFront, SNS/SQS/EventBridge |
| Identity | Keycloak (OIDC, per-tenant realms-lite via org claims) |
| Tenancy | Shared DB, shared schema, `tenant_id` + RLS; cell-based sharding at scale |
| Eventing | Transactional outbox → EventBridge bus → SQS consumers; sagas for long-running import flows |

### 1.6 Success Metrics (Year 1)
- 40 paying tenants, 1,200 active users, BDT 3.2 Cr ARR (~USD 260k)
- < 2% landed-cost variance between forecast and actual for mature tenants
- Import file cycle time reduced 25% vs. baseline
- 99.9% uptime, P95 API latency < 300 ms

---

## 2. MVP Scope (Months 0–6)

**Goal:** a tenant can run a complete import file end-to-end and know true landed cost.

| Area | MVP Includes | Deferred |
|---|---|---|
| Procurement | Vendor registration/qualification, PR, PO, RFQ + quotation comparison, GRN, invoice (3-way match), basic budget check | RFP, contract mgmt, vendor scorecard automation, e-bidding portal |
| Import | Import PO, PI/CI/Packing List, HS code master, shipment tracking (manual milestones), BL/AWB, C&F agent mgmt, customs clearance checklist | Carrier API container tracking, permit workflow automation |
| Trade Finance | LC register, margin tracking, payment schedule, TT payments | Back-to-back LC, import loans, SWIFT GPI integration |
| Customs & Tax | BD duty calculator (CD/RD/SD/VAT/AIT/AT), assessment capture, duty payment tracking | Duty forecasting ML, SRO rule engine automation |
| Landed Cost | Shipment cost sheet, allocation engine (value/weight/qty drivers), actual landed cost to inventory | Forecast variance analytics, what-if simulator |
| Feasibility | v1 heuristic score (historical averages + rules) | ML-based prediction models |
| Platform | Multi-tenancy, RBAC, approval engine (sequential/amount-based), audit trail, notifications, dashboards (5 core reports) | Custom workflow designer, advanced analytics, API marketplace |

**MVP team:** 1 PM, 1 architect, 4 backend, 3 frontend, 1 QA, 1 DevOps, 1 domain consultant (customs/C&F).

## 3. Enterprise Scope (Months 7–18)
- Full RFP & contract lifecycle, supplier portal (self-service registration, bid submission, invoice upload)
- Back-to-back LC, import loan & margin financing, bank integration (statement import, LC advice parsing)
- ML engines: cost forecasting, duty forecasting, delay prediction, supplier risk, vendor & purchase recommendations
- Container tracking via carrier APIs (Maersk, MSC, project44/Vizion aggregators)
- Advanced workflow designer (graphical, tenant-configurable), delegation of authority matrix
- Inventory & finance deep integration: batch/lot/serial, FIFO/weighted-average valuation, accrual automation, ERP connectors (SAP B1, Odoo, Tally Prime, ERPNext)
- OpenSearch-powered global search, saved analytics, scheduled report distribution
- SOC 2 Type II, ISO 27001 alignment; data residency options

## 4. Pricing Strategy

### 4.1 Model — hybrid: base subscription + usage + modules
| Plan | Monthly (BDT) | Users | Import files/yr | Modules |
|---|---|---|---|---|
| Starter | 25,000 | 10 | 120 | Procurement + Import + Landed Cost |
| Professional | 60,000 | 30 | 500 | + Trade Finance + Analytics |
| Enterprise | 150,000+ | Unlimited | Unlimited | All + ML engines + SSO + API + SLA 99.9% |

- Add-ons: supplier portal (BDT 10k/mo), extra import files (BDT 150/file), ERP connector (BDT 15k/mo each), dedicated DB cell (custom).
- Annual prepay: 2 months free. Implementation/onboarding: BDT 1–6 lakh one-time (data migration, duty structure setup, training).
- USD pricing for international tenants at ~1.3× parity (Starter $349, Pro $799, Enterprise $1,999+).

### 4.2 Rationale
Anchored against cost of errors (a single mis-assessed consignment often exceeds annual Starter price), not against software comparables. Land with Import + Landed Cost (sharpest pain), expand to full S2P.

## 5. Bangladesh Market Strategy
- **TAM:** ~8,000 active industrial importers (BGMEA ~4,600 member factories, BTMA ~1,500 mills, pharma ~250, food ~700, large traders ~1,000+). SAM (organized, >50 imports/yr): ~2,500 firms.
- **Beachhead:** Dhaka/Gazipur/Narayanganj RMG & textile importers of fabric, yarn, dyes-chemicals, and machinery — highest LC and back-to-back volume, existing relationship from Garments ERP network.
- **Channels:** direct enterprise sales; partnerships with C&F agent associations (Chattogram & Benapole), banks' trade desks (co-marketing LC tooling), chartered accountant firms, BGMEA/BKMEA/BTMA seminars.
- **Localization:** Bangla UI option, BDT-first with multi-currency, NBR tariff database preloaded yearly with Finance Act updates, bKash/bank transfer billing, local data residency narrative (AWS ap-south-1 Mumbai now; local cell later).
- **Regulatory tailwinds:** NBR's ASYCUDA World digitization, Bangladesh Single Window (BSW) rollout — position as the enterprise-side complement; build BSW/ASYCUDA data exchange when APIs open.

## 6. Competitor Analysis
| Competitor | Strength | Gap TradeFlow exploits |
|---|---|---|
| SAP Ariba / S4 MM | Global S2P depth | Cost (crores), no BD duty cascade, no LC/landed-cost depth, long implementations |
| Oracle Fusion Procurement | Suite integration | Same as above; overkill for mid-market |
| Coupa / Zycus / GEP | Spend analytics, sourcing | No import/customs/trade-finance domain; USD pricing |
| Odoo / ERPNext | Affordable, flexible | Landed cost is journal-level only; no LC lifecycle, duty cascade, feasibility scoring; heavy customization burden |
| Tally Prime + Excel (status quo) | Ubiquitous, cheap | No process control, no analytics, error-prone — our true competitor |
| Local custom software houses | Cheap, bespoke | No product depth, no upgrades, key-person risk |

**Positioning statement:** "The only platform that tells you your true landed cost — before you sign the PO."

## 7. Sales & Go-To-Market
**Phase 1 (0–6 mo): Design partners.** 5 tenants at 70% discount, weekly feedback loops, co-developed duty structures. Success story documentation.
**Phase 2 (6–12 mo): Beachhead expansion.** Direct sales team (2 AEs + 2 SEs), C&F/bank channel activation, BGMEA events, case-study-led content (Bangla + English), LinkedIn + trade press.
**Phase 3 (12–24 mo): Verticalization & region.** Pharma pack (DGDA import permit workflows), food (BSTI/quarantine), construction (project-based budgeting); expand to Sri Lanka, Pakistan, Kenya, Nigeria (similar duty-cascade economies).

**Sales motion:** land-and-expand. Entry via free "Landed Cost Health Check" (we recompute their last 20 consignments and show leakage) → Starter → module expansion. Sales cycle target 45–60 days mid-market, 90–120 enterprise.

## 8. SaaS Revenue Model
- **Streams:** subscriptions (75%), implementation & training services (15%), usage overages + add-ons (7%), partner/API licensing (3%).
- **Unit economics targets:** gross margin ≥ 78% (infra cost per tenant ≤ BDT 4,500/mo at steady state), CAC payback ≤ 9 months mid-market, NRR ≥ 115% via module expansion, logo churn < 8%/yr (import data gravity creates strong lock-in: historical duty rates, supplier scorecards, cost baselines).
- **Financial trajectory:** Y1 BDT 3.2 Cr ARR / 40 tenants → Y2 BDT 11 Cr / 120 tenants → Y3 BDT 28 Cr / 260 tenants + regional.
