# ProcureFlow — Implementation BRS Suite

**Version 1.0** | August 2026 | Status: Baseline for implementation

This suite consolidates the ProcureFlow v1.0 blueprint (13 documents) and the
ProcureFlow v2.0 Batch-1 volumes (V00–V04) into a **single authoritative,
implementation-ready BRS**, aligned for delivery on the **Modulus framework**
(.NET 10 modular monolith, per-module DbContext, Modulus.Mediator, EF Core,
OpenIddict identity, transactional outbox + sagas).

## Document map

| # | Document | Contents |
|---|----------|----------|
| 1 | [BRS-Core.md](BRS-Core.md) | Document control & precedence, executive summary, goals & traceability, actors/org/governance, business lifecycles, MVP functional scope, NFRs, out-of-scope, open decision log, v1-vs-v2 conflict resolutions |
| 2 | [BRS-Business-Rules.md](BRS-Business-Rules.md) | Consolidated business-rule register (one ID namespace, ~150 rules) + deterministic computation reference (duty cascade, allocation, feasibility scoring) + default tolerances |
| 3 | [BRS-Phasing-Implementation.md](BRS-Phasing-Implementation.md) | Phase 1 (MVP, months 0–8), Phase 2 (9–18), Phase 3 (19–36) deliverables & exit criteria; Modulus module mapping, framework reuse, gap backlog, dependency graph, testing strategy, risks |

## Source documents

| Source set | Location | Status |
|---|---|---|
| ProcureFlow v1.0 "Complete Production Blueprint" (docs 00–12) | `samples/files_1/` | Superseded by v2 where v2 covers the topic; sole source for domain-module detail (procurement, import, trade finance, customs, landed cost, engines, reporting, database, UI) |
| ProcureFlow v2.0 Batch 1 (V00–V04) | `samples/files_2/` | Current. Batches 2–6 (V05–V28) not yet written; v1 fills those gaps |

## Precedence rules

1. **v2.0 (files_2) wins** wherever it covers a topic (business model, MVP scope,
   pricing, NFRs, platform foundation, workflow/notification engines).
2. **v1.0 (files_1) fills** all domain gaps (modules 03–08, architecture 09–11,
   UI 12) until v2 batches 2–6 are delivered.
3. **Modulus framework alignment overrides both** on technology decisions
   (runtime, data access, identity, eventing) — see decision log D-01…D-08 in
   BRS-Core §9.
4. Every deviation from a source document is recorded in the conflict-resolution
   appendix (BRS-Core Appendix A) or the decision log — nothing is silently changed.

## Reading order

- **Business stakeholders:** BRS-Core §2–§5 → BRS-Business-Rules (skim per module)
- **Implementation team:** BRS-Core §6–§9 → BRS-Business-Rules (full) → BRS-Phasing-Implementation (full)
- **QA:** BRS-Business-Rules (validation rules & edge cases) + BRS-Phasing exit criteria
