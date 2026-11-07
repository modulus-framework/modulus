---
sidebar_position: 3
title: Analytics governance
description: Consent, plan gating, circuit breakers, quality checks, retention, catalogs and lineage for analytics data.
---

# Analytics governance

Analytics data leaves the application, so the layer adds checks at each step. Each check can be turned on or left at its default.

## Consent

`IAnalyticsConsent` decides, per tenant, whether data may be captured and delivered. It is checked twice:

- at **capture**, so nothing is queued for a tenant that has not consented;
- at **delivery**, so a record queued before consent was withdrawn is held back, not dead-lettered.

The default grants everything, so an existing application works unchanged. Register your own `IAnalyticsConsent` to enforce a policy.

## Plan gating

A dataset may name a feature in `RequiredFeature` (for example `analytics.advanced`). Data for a tenant is exported only when its plan includes that feature. The check goes through `IAnalyticsEntitlement`.

## Circuit breaker

Each sink has a circuit breaker. Transient failures open the circuit, so a failing store stops receiving writes for `OpenDuration`, then one trial write decides whether it recovers. Permanent failures count as a sink that answered. Configure it under `Analytics:CircuitBreaker`:

```json
"Analytics": {
  "CircuitBreaker": { "FailureThreshold": 5, "OpenDuration": "00:01:00" }
}
```

## Quality checks

Quality rules run against a dataset and report findings, not just counts. Rules include the maximum allowed null rate per field and records dated in the future. `POST /analytics/quality` runs them. Findings go to every `IAnalyticsQualityFindingSink`; by default they are logged at warning level.

## Reconciliation

`POST /analytics/reconciliations` compares, per tenant, the number of records in the operational store with the number delivered. It reports the difference, which incremental sync cannot see for hard deletes.

## Retention and erasure

Each dataset states how long a provider should keep its data (`RetentionDays`). When a company's data is erased through the tenant lifecycle, the analytics layer removes its rows (`AnalyticsTenantDataHandler`). Erasure reaches the ClickHouse TTL and the delivery queue; Parquet files are erased with the lake's tools.

## Export audit

Exports are recorded in the hash-chained security audit under the `analytics` category.

## Catalogs and schemas

Optional packages publish the layer's metadata to the tools data teams use:

| Package | Publishes |
|---------|-----------|
| `Modulus.Analytics.OpenMetadata` | Tables, typed columns, nullability and classification into an OpenMetadata catalog |
| `Modulus.Analytics.Apicurio` | Each dataset's JSON Schema as an artifact in an Apicurio schema registry |
| `Modulus.Analytics.OpenLineage` | Lineage events (START, COMPLETE, FAIL) for each backfill, sent to a Marquez-compatible receiver |

`Modulus.Analytics.DuckDB` reads the Parquet lake in place to count distinct records per tenant for reconciliation.

## Machine learning

MLflow is not built into the layer. The runbook `docs/runbooks/analytics-ml-mlflow.md` in the repository describes how to use it with the published datasets.
