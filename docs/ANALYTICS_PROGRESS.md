# Analytics Integration Layer: progress

Status as of 2026-11-04. The full plan and per-feature notes are in [DOCUMENTATION.md](DOCUMENTATION.md#analytics-integration-layer-plan-2026-11-03). This page is the short tracker: what is done, what is open, and what is next.

Branch: `claude/optimistic-noether-qvkv7n` (modulus). Packages live in `src/analytics/`.

## Tool policy

Every tool and NuGet package must be open source (OSI licence), production-proven, and pass the licence gate before pinning. Results are in [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

| Item | Decision |
|---|---|
| ClickHouse.Driver 1.5.0 (MIT), Testcontainers.ClickHouse (MIT), Parquet.Net 6.1.0 (MIT) | Adopted and pinned |
| JsonSchema.Net | Rejected (Open Source Maintenance Fee EULA) |
| Apache.Arrow, Apache.Avro, DuckDB.NET.Data, NJsonSchema | Cleared, not pinned |

## Phases

| Phase | Scope | Status |
|---|---|---|
| 0 | Plan, licence gate | Done |
| 1 | Contracts, catalog, schema versions, JSON Schema export, sink conformance | Done |
| 2 | Capture, durable delivery queue, ClickHouse sink | Done. Open: messaging sink, EF migration sample, `SchemaVersion` on outbox rows |
| 3 | Backfill, incremental sync, Parquet sink | Done. Open: backfill run against SQL Server |
| 4 | Governance | Done (see below) |
| 5 | Operability: CLI `add-analytics`, catalog and freshness endpoints (done); `analytics-admin` in modulus-ui (open, separate repository) | In progress |
| 6 | Enterprise providers: Debezium reader, Airflow/dbt trigger, Superset guest tokens, Doris sink, lakehouse runbooks | Done (live Debezium, Kafka, Doris, Airflow, Superset and Iceberg not run here; see DOCUMENTATION.md) |

## Phase 4 detail

| Item | Status | Commit |
|---|---|---|
| Freshness and monitoring (metrics, health check, `GET /analytics/freshness`) | Done | earlier |
| Reconciliation (per-tenant counts, `POST /analytics/reconciliations`) | Done | earlier |
| Dataset permissions (`analytics:manage:{dataset}`) and plan gating (`RequiredFeature`) | Done | `51ec852` |
| Export audit through `ISecurityAuditLog` (hash-chained, category `analytics`) | Done, tests pass on net8.0 and net10.0 | `0af9463` |
| Data-quality findings beyond counts (`MaxNullRate`, future records; `POST /analytics/quality`) | Done, 6 core, 2 EF, 2 management and 1 ClickHouse integration test pass | `975c60e` |
| Retention and erasure propagation (`AnalyticsTenantDataHandler`, `IAnalyticsErasableSink`; ClickHouse TTL, queue purge; Parquet by the lake's tools) | Done, 5 unit tests and 2 ClickHouse integration tests pass | `185034f` |
| Lineage and OpenLineage emitter (`Modulus.Analytics.OpenLineage`; START, COMPLETE, FAIL per backfill; verified against Marquez 0.50.0) | Done, 10 unit tests and 2 Marquez integration tests pass | `a00c5dc` |

## Test status

Last full run of the analytics suites (both frameworks where applicable):

| Suite | Result |
|---|---|
| Modulus.Analytics.Tests | 62 pass |
| Modulus.Analytics.EntityFrameworkCore.Tests | 91 pass |
| Modulus.Analytics.Management.Tests | 36 pass |
| Modulus.Analytics.ClickHouse.Tests | 23 pass |
| Modulus.Analytics.Parquet.Tests | 25 pass |
| Integration: ClickHouse (real server) | 16 pass |
| Modulus.Analytics.OpenLineage.Tests | 8 pass (real HTTP receiver) |
| Modulus.Analytics.Airflow.Tests | 8 pass (real HTTP, fake Airflow) |
| Modulus.Analytics.Superset.Tests | 6 pass (real HTTP, fake Superset) |
| Modulus.Analytics.Debezium.Tests | 12 pass |
| Modulus.Analytics.Doris.Tests | 9 pass (real HTTP, fake frontend and backend) |
| Modulus.Cli.Tests (add-analytics, 6 new) | 677 pass |
| Integration: OpenLineage with Marquez 0.50.0 | 2 pass (re-run after the run-observer change) |
| Integration: EF (real PostgreSQL 16) | 8 pass (re-run after the run-observer change) |

`dotnet format --verify-no-changes` is clean for the analytics projects. Each changed project builds with 0 warnings.

## Verification

- Verified on this commit: full-solution build (`dotnet build modulus.slnx`, 0 errors, 0 warnings); full unit suite (`--filter "Category=Unit"`, 95 assembly runs on net8.0 and net10.0, 0 failures).

## Next steps

1. Phase 5: `analytics-admin` in modulus-ui (separate repository, blocked on push access).
2. Live verification of Phase 6 against real Debezium, Kafka, Doris, Airflow, Superset and Iceberg (needs disk space and the Docker Hub pull limit cleared).

## Decisions to know about

- Commit trailers: no model name in commit messages (repo rule). The `Claude-Session` trailer is kept.
- Denied requests are not audited by the analytics management API; the framework's access-decision audit covers them.
- Audit entries go to the host chain (`TenantId` null), because one request can span tenants.
