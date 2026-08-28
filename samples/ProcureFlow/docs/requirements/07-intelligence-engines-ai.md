# 07 — Intelligence Engines & AI/Analytics

Four engines share a layered design: **deterministic core (rules/formulas) → statistical layer (historical aggregates) → ML layer (Enterprise plan)**. Every engine output is a persisted, versioned snapshot with full input lineage — decisions must be reproducible for audit.

```
                        ┌────────────────────────────┐
 Domain events ───────▶ │  Feature Store (Postgres   │
 (files closed, costs   │  marts + Redis online keys)│
  finalized, milestones)└──────────┬─────────────────┘
                                   │
        ┌──────────────┬───────────┼──────────────┬───────────────┐
        ▼              ▼           ▼              ▼               ▼
  Landed Cost     Cost Forecast  Feasibility   Supplier Risk   Recommenders
  Engine (det.)   Engine         Engine        Engine          (vendor/purchase)
```

## 7.1 Landed Cost Engine (deterministic)

**Responsibility:** given a cost-element set + allocation config + line drivers → exact allocations and unit costs (formulas in doc 06 §6.6); plus the **duty calculator** (cascade + SRO + tariff-value, doc 06 §6.1).

**Design (.NET):**
```csharp
// Domain service, pure & deterministic — fully unit-testable
public interface ILandedCostEngine {
    DutyCalcResult ComputeDuty(DutyCalcRequest r);          // per BoE line
    CostSheetResult Allocate(CostSheetInput sheet);          // whole sheet
}
public record DutyCalcRequest(HsCode Hs, Money Cif, decimal CustomsFxRate,
    decimal LandingChargePct, DateOnly TaxPointDate, TenantTaxProfile Profile,
    CooPreference? Coo, IReadOnlyList<SroBenefit> Benefits, QtyInfo Qty);
public record DutyCalcResult(Money Av, IReadOnlyDictionary<DutyComponent, DutyLine> Lines,
    Money LandedDutySubtotal, Money RecoverableSubtotal, RateLineage Lineage);
```
- Rate resolution via repository keyed `(hs, component, tax_point_date)`; `RateLineage` stores the exact rate-row ids (DS-04).
- Property-based tests (FsCheck) assert invariants: monotonicity (rate↑ ⇒ TTI↑), allocation conservation (Σ allocated = element amount), rounding residual ≤ 1 unit.
- Exposed three ways: in-process (cost sheet finalization), API (`POST /v1/calc/duty` for UI what-if), and batch (Finance-Act impact runs).

## 7.2 Cost Forecasting Engine

**Responsibility:** predict each cost element for a prospective import: freight, insurance, duty components, port/C&F/bank charges, transit time → expected landed cost distribution (P50/P80), used by feasibility, RFQ bid normalization, and cost-sheet seeding.

**v1 statistical layer (MVP):**
- Freight: lane (origin port × dest port × mode × container type) median of last 6 months tenant + anonymized cross-tenant pool (opt-in benchmarking), inflation-adjusted by lane index; LCL/air per kg-or-CBM chargeable weight.
- Duty: deterministic from current rate tables (+ flag if Finance Act window — June — adds uncertainty band).
- Port/C&F: per-container & per-BoE medians by port and agent rate card.
- Bank: bank's charge schedule (master) + margin opportunity cost; UPAS/usance interest from tenor × bank rate.
- FX: forward curve proxy = spot + tenant-configurable annual drift (default from 12-month historical BDT depreciation); applied to maturity date.
- Transit: lane median + port dwell median ⇒ timeline estimate (ETD→factory).
**Output:** `cost_forecast` snapshot (element → {p50, p80, basis, sample_n, staleness}); low-sample elements (< 5 observations) fall back to tenant defaults and are flagged low-confidence.

**ML layer (Enterprise):** gradient-boosted models (LightGBM) per target — freight rate, dwell days, assessment uplift % — features: lane, month, carrier, container type, HS chapter, supplier, port congestion index (external feed), historical variance. Trained per-cell on pooled anonymized data, tenant-specific residual correction. Served via a Python FastAPI sidecar service (ECS) called by .NET with 150 ms budget + statistical fallback on timeout.

## 7.3 Purchase Feasibility Engine

**Responsibility:** at PO submit (PO-03), produce score 0–100 + decision evidence in < 3 s.

**Inputs:** PO lines (item, HS, qty, price, Incoterm, supplier, ports, payment mode/tenor), Cost Forecast outputs, Supplier Risk score, item sales/standard price (margin basis), budget state, import plan alignment.

**Computation:**
```
expected_landed_unit = forecast P50 per line (doc 07.2)
margin_pct = (selling_or_standard_price − expected_landed_unit) / selling_or_standard_price
timeline_days = production_lead + transit_p50 + clearance_p50 + inland
score = Σ weighted factors (tenant-tunable weights, defaults):
  Margin adequacy (vs. category target margin)        30
  Cost competitiveness (price vs. last-3 imports
    & vs. best alternative supplier landed)           20
  Supplier risk (inverse of risk score)               20
  Timeline fit (need-by vs. estimated arrival)        15
  Historical variance (item/lane forecast accuracy)   10
  Plan & budget alignment                              5
Each factor normalized 0–100 via tenant-calibrated breakpoints.
```
**Output snapshot (stored on PO):** score, factor table, expected landed cost per line (P50/P80), expected margin %, timeline with milestone estimates, top-3 risk flags (e.g., "Supplier OTD 71% last 6 mo", "Finance Act rate-change window", "LC margin will exceed facility headroom"), and counterfactual hints ("Supplier B landed cost est. 4.2% lower for line 2").
**Governance:** below threshold → CFO override path (reason coded); quarterly calibration report: score deciles vs. realized margin/delay — weights tuned per tenant.

## 7.4 Supplier Risk Engine

**Responsibility:** continuous 0–100 risk score per vendor (higher = riskier) + grade and watch flags.

**Factor model (v1):**
| Pillar (weight) | Signals |
|---|---|
| Performance 35 | OTD trend, quality rejection rate, shipment-doc discrepancy rate, amendment-cause attribution |
| Financial/Commercial 20 | advance-payment exposure outstanding, dependence (our spend share growth), price volatility vs. category |
| Compliance 20 | KYC doc expiry, sanctions/blacklist screening hits, COO/permit irregularities |
| Concentration 15 | single-source criticality (items with no qualified alternative), country concentration |
| External 10 | country risk index (configurable feed), port congestion of supplier's lane |
Decay-weighted (recent months weigh more); event-driven recompute (evaluation events, discrepancies) + nightly batch. **Watch rules:** step change > 15 pts, grade crossing, sanctions hit → immediate alerts + auto-proposal (On-Hold) for extreme cases.
**ML layer:** survival model for "probability of late shipment > 14 days within next order" — surfaces as a per-PO risk flag in feasibility.
**Data:** `supplier_risk_score` (vendor, as_of, pillar scores jsonb, total, grade, drivers jsonb) — full history retained for trend charts and model audit.

## 7.5 Recommendation Engines (Enterprise)
- **Vendor Recommendation:** for a sourcing case → rank qualified vendors by predicted landed cost (forecast engine per candidate's lane/Incoterm history) × risk × capacity signals; explanation strings rendered in RFQ invite screen.
- **Purchase Recommendation:** reorder advisor — consumption run-rate (from GRN/issue data or ERP feed) + lead-time distribution + price/duty trend → "order 12,000 kg in July; expected landed 412 BDT/kg; waiting until August risks +3.1% (Finance Act + freight seasonality)". Delivered as a workbench feed with one-click PR creation.

## 7.6 Duty Forecasting, Price Trend & Delay Prediction
- **Duty forecast:** scenario engine over rate tables (proposed Finance Act rates entered as draft scenario → portfolio impact on open POs/plan); plus historical assessment-uplift model per HS/port (expected assessed vs. declared).
- **Price trend:** per item/category index from PO/bid history (hedonic adjustment for qty breaks), exposed in bid analysis ("bid is 6% above 3-mo trend").
- **Delay prediction:** classifier (late > 7 days?) per shipment at booking time; features: lane, carrier, month, supplier OTD, transshipment count, port dwell index. Output drives proactive alerts and feasibility timeline P80.

## 7.7 MLOps
- Training: scheduled ECS tasks (Python, LightGBM/scikit), data from reporting replica; experiments tracked (MLflow on ECS + S3 artifacts); model registry with per-cell deployment tags.
- Serving: FastAPI inference service, blue/green; every prediction logged (features hash, model version, output) → drift dashboards (PSI on key features) in Grafana; auto-fallback to statistical layer on drift breach or timeout.
- Privacy: cross-tenant pooling only on anonymized, aggregated lane/HS features; per-tenant opt-out honored at feature-build time; no tenant-identifiable data in shared models.
