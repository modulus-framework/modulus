# V00 — Executive Summary & SaaS Business Model
Covers master topics: 00 Executive Summary · 129 SaaS Subscription · 130 Billing · 131 Tenant Provisioning · 132 White Label · 133 Feature Flags

---

## 1. Vision

TradeFlow is an **AI-native, cloud-native, multi-tenant Source-to-Pay and Import-to-Inventory platform** purpose-built for Bangladesh and South Asia. Global suites (Ariba, Coupa, Oracle, Ivalua, Jaggaer, GEP) treat import operations, LC finance, and NBR duty structures as bolt-ons or consulting projects; local tools (Tally + Excel + C&F agent phone calls) treat them as somebody else's problem. TradeFlow makes the **import file — from proforma invoice to landed cost posting — a first-class digital object**, wraps global-grade S2P around it, and embeds AI at every decision point rather than as a chatbot veneer.

**One-line positioning:** *"The system of record and system of intelligence for everything a South Asian enterprise buys, imports, finances, clears, and costs."*

## 2. Why now, why Bangladesh

- **$70B+ annual import economy**, ~8,000 active industrial IRC holders, 4,500+ RMG factories, growing pharma/food/construction verticals — nearly all running procurement on spreadsheets and imports on paper files.
- **NBR digitization** (ASYCUDA World, online Mushak, e-BIN) creates machine-readable rails that legacy tools ignore.
- **Bangladesh Bank tightening** on IMP reporting, LC monitoring, and forex discipline makes auditable trade-finance workflows a compliance necessity, not a luxury.
- **LDC graduation (2026)** erodes preferential duty access → landed-cost precision and SAFTA/APTA/COO optimization become CFO-level concerns.
- **AI inflection:** OCR + LLM document processing finally makes it economical to digitize the messy paper reality (PI, CI, PL, BL, BoE, bank documents) that killed previous ERP attempts.

## 3. Differentiators

| # | Differentiator | Why competitors can't easily copy |
|---|---|---|
| 1 | **BD-native duty & landed cost engine** — CD/RD/SD/VAT/AIT/AT cascade, SRO benefits, tariff values, rate lineage, reproducible calculations | Requires NBR tariff data operations + domain depth; global vendors won't invest for one market |
| 2 | **Import File Command Center** — single aggregate spanning PI→LC→shipment→BoE→GRN→cost sheet | Global suites split this across 4+ modules and 2+ products |
| 3 | **Trade finance depth** — LC/BTB/UPAS lifecycle, margin as restricted cash, IMP matching, maturity calendar | Banks' portals show their side only; ERPs model LCs as attachments |
| 4 | **Pre-PO Purchase Feasibility Engine** — margin/risk/timeline score gates PO submission | Requires the landed-cost engine as substrate |
| 5 | **AI document processing tuned for BD trade paper** — bilingual OCR, BoE/BL/PI extraction, auto-reconciliation | Data moat grows with every tenant |
| 6 | **Priced for BD** — BDT pricing at 5–15% of Ariba TCO, bKash-friendly billing, local support | Global vendors' cost structure forbids it |

## 4. Product Scope Tiers

### MVP (months 0–8) — "Import-first S2P core"
| Included | Deferred |
|---|---|
| Multi-tenancy, org, identity (SSO/MFA), permissions | Reverse auction |
| Workflow + notification engines | Dashboard/report builder (canned reports ship) |
| Vendor mgmt + qualification + supplier portal (respond/acknowledge/ASN) | Full supplier collaboration suite (forecast sharing) |
| PR → RFQ → comparison → PO (+ blanket PO) | RFI/RFP, framework agreements |
| GRN + basic QC + invoice 3-way match + budget control | Service/CAPEX/project procurement variants |
| Import file, PI/CI/PL, HS codes, shipment, container, BL/AWB, insurance | Vessel tracking API integration (manual milestones in MVP) |
| Customs: BoE, duty cascade, assessment variance, C&F mgmt | ASYCUDA direct integration (mirror-entry in MVP) |
| LC + TT + margin + payment calendar | BTB LC, import loans, guarantees, forex desk |
| Landed Cost Engine + cost sheets + inventory valuation handoff | Full WMS (bin/rack), fixed assets |
| AI: OCR document capture, duty forecast, feasibility engine v1 | Copilot/chat assistant, fraud detection, demand forecasting |
| Canned dashboards + OpenSearch omni-search | Semantic search, white label |
| Mobile-responsive web + approver PWA | Native offline mobile apps |

### Enterprise (months 9–24)
Everything deferred above, plus: reverse auctions, AI copilot with tenant-grounded RAG, fraud/duplicate detection, demand forecasting, report/dashboard builder, WMS depth, fixed assets, white label, offline mobile, ASYCUDA/Bangladesh Bank integrations as APIs open, marketplace of pre-integrated banks/insurers/forwarders.

## 5. Target Segments & Verticals
Primary: RMG & textile (beachhead — highest import intensity, BTB LC natives), pharmaceuticals (DGDA permits, cold chain), food & agro processing, construction & engineering, chemicals & plastics, electronics/appliance assembly, trading & distribution houses. Secondary geography sequence: Bangladesh → Sri Lanka & Nepal (similar LC-driven import regimes) → Vietnam & Indonesia (localization packs) → Pakistan → India (last: competitive density) → GCC (trading houses).

## 6. Competitive Landscape

| Competitor | Strength | Gap TradeFlow exploits |
|---|---|---|
| SAP Ariba / S4 | Global S2P depth, network | No BD duty/LC depth; $250K+ TCO; no local paper reality |
| Oracle Procurement Cloud | Suite integration | Same as above; weak import ops |
| Coupa / Ivalua / Jaggaer / GEP | BSM analytics, sourcing | Indirect-spend DNA; imports are out of scope |
| MS Dynamics 365 SCM | Mid-market reach, partners | Landed cost is basic; BD compliance = partner custom code |
| Odoo / ERPNext Enterprise | Price, flexibility | Import/trade finance modules shallow; duty cascade absent; multi-tenant SaaS ops on customer |
| Local ERPs (Troyee, PrideSys, LinesPay-adjacent) | Local presence | Desktop-era architecture, no AI, weak procurement workflow |
| Status quo (Tally + Excel + WhatsApp) | Free, familiar | No control, no audit, no intelligence — our real competitor |

## 7. Business Model — SaaS Subscription (topic 129)

### 7.1 Plans
| Plan | Monthly (BDT) | Users | Import files/yr | Key gates |
|---|---|---|---|---|
| **Starter** | 30,000 | 15 | 120 | Core P2P + import + LC; canned reports; email support |
| **Professional** | 75,000 | 50 | 600 | + Sourcing suite, budget control, AI doc processing, feasibility engine, supplier portal unlimited, API access |
| **Enterprise** | 180,000+ (quoted) | Unlimited | Unlimited | + SSO/SAML, white label, report builder, copilot, dedicated success manager, 99.9% SLA, sandbox tenant |
| **Group/Holding** | Custom | — | — | Multi-company consolidation, cross-company analytics, volume pricing |

USD parity for regional tenants (Starter $349 / Pro $849 / Ent $1,999+). Annual prepay −15%. Add-ons: extra import-file packs, OCR page packs (AI processing metered per 1,000 pages), SMS/WhatsApp bundles, additional environments, premium onboarding (fixed-fee implementation 1–3 months by tier).

### 7.2 Subscription mechanics
- Subscription aggregate: `subscription(tenant_id, plan_id, status, term, seats, entitlements jsonb, mrr, renewal_at)`; states: Trial(30d) → Active → PastDue → Suspended → Cancelled → Churned; grace 14 days with read-only degradation before suspension (data never deleted before 90-day retention window + export offered).
- Entitlement checks are **feature flags resolved at login** (see §10) — no hard-coded plan checks in domain code.
- Usage metering events (`ImportFileOpened`, `OcrPageProcessed`, `SmsSent`) flow through the standard event pipeline into a metering ledger; overages billed monthly in arrears.

## 8. Billing (topic 130)
- Billing engine: invoice generation (BDT Mushak 6.3-compliant tax invoice with 15% VAT on SaaS, or reverse-charge/zero-rated for export-of-service tenants), proration on mid-term upgrades (immediate) and downgrades (next term), dunning sequence D0/D3/D7/D10 (email → email+SMS → account owner call task → suspend).
- Payment rails: bank transfer (BEFTN/RTGS) primary for enterprise; SSLCommerz gateway (cards, bKash, Nagad, Rocket) for Starter/Pro auto-debit mandates; USD via Stripe for regional tenants.
- Revenue recognition: monthly ratable; deferred revenue schedule per subscription; FinOps dashboard (MRR, NRR, churn, LTV:CAC, gross margin per tenant including AWS + OCR + SMS COGS).

## 9. Tenant Provisioning (topic 131)
Self-serve trial → provisioning saga:
1. `TenantSignupRequested` → create tenant row + Keycloak org group + default roles → seed reference data (BD chart-of-accounts template, NBR tariff snapshot, Incoterms, currencies, BD holiday calendar, workflow templates, DoA template) → create OpenSearch filtered aliases → emit `TenantProvisioned` (target < 60 s).
2. Sales-assisted enterprise: provisioning checklist adds SSO federation, IP allowlist, custom domain (white label), sandbox tenant, data migration workspace.
3. Deprovisioning: export bundle (all documents + attachments + audit log as signed archive) → 90-day cold retention → crypto-shred tenant DEK.
- Cell architecture: tenants assigned to a cell (DB cluster + service fleet); Group/Enterprise tenants may get dedicated cells; `platform.tenant.cell_id` routes at the edge.

## 10. Feature Flags (topic 133)
- Flag service (OpenFeature-compatible; flags in `platform.feature_flag` + Redis cache, SSE push on change). Dimensions: plan entitlement, tenant override, percentage rollout, user-role targeting, kill switch.
- Conventions: every Enterprise feature ships dark behind a flag; flags expire (mandatory `sunset_at` review); domain layer never reads flags — application layer gates commands/queries; UI reads a resolved flag map from the BFF session.

## 11. White Label (topic 132) — Enterprise/Group
Custom domain (CNAME + ACM cert automation), logo/palette/typography theme tokens per tenant (CSS variables layer above the design system), branded email/SMS sender identities, PDF letterheads per company, supplier-portal branding inherits buyer tenant theme, "Powered by TradeFlow" footer removable at Group tier. Explicit non-goals: per-tenant code forks; all white-labeling is data/theme-driven.

## 12. Go-to-Market
Phase 1 (mo 0–8): 10 design partners (5 RMG, 2 pharma, 2 trading, 1 construction) at 50% founder pricing, weekly feedback loops. Phase 2 (mo 9–18): direct sales (2 AEs + solutions engineer), channel via C&F agent networks + trade-finance bank partnerships (banks refer clients for LC discipline), BGMEA/BKMEA/DCCI seminars, "Landed Cost Health Check" audit as lead magnet. Phase 3 (mo 18+): Sri Lanka/Nepal partners, regional localization packs, marketplace revenue share with insurers/forwarders.

## 13. Revenue Model (planning case)
| Year | Tenants (paying) | Avg MRR (BDT) | ARR (BDT Cr) | Notes |
|---|---|---|---|---|
| Y1 | 45 | 62,000 | 3.3 | Design partners convert; Starter-heavy |
| Y2 | 140 | 78,000 | 13.1 | Pro dominates; first Group deals |
| Y3 | 320 | 92,000 | 35.3 | Regional expansion begins; add-on/usage revenue ≈ 18% of total |
Unit economics targets: gross margin ≥ 72% (AWS + AI COGS discipline), CAC payback ≤ 14 months, NRR ≥ 115% via seat expansion + usage + tier upgrades.

## 14. Success Criteria for this Blueprint
PMs can cut a backlog per volume; designers can build every screen from V28 screen specs + module UI sections; engineers implement from business rules + schema + event catalog without guessing; QA derives test cases from validation rules + edge cases; DevOps deploys from V28; auditors trace every rupee of duty from BoE line to GL posting.
