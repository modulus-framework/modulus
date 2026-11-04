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
| 4 | Governance | In progress (see below) |
| 5 | Operability: CLI `add-analytics`, `analytics-admin` in modulus-ui, catalog and freshness endpoints | Not started |
| 6 | Enterprise providers: Debezium, Airflow/dbt triggers, Superset helper, Doris, lakehouse runbooks | Not started |

## Phase 4 detail

| Item | Status | Commit |
|---|---|---|
| Freshness and monitoring (metrics, health check, `GET /analytics/freshness`) | Done | earlier |
| Reconciliation (per-tenant counts, `POST /analytics/reconciliations`) | Done | earlier |
| Dataset permissions (`analytics:manage:{dataset}`) and plan gating (`RequiredFeature`) | Done | `51ec852` |
| Export audit through `ISecurityAuditLog` (hash-chained, category `analytics`) | Done, tests pass on net8.0 and net10.0 | `0af9463` |
| Data-quality findings beyond counts | Open | |
| Retention and erasure propagation (`AnalyticsTenantDataHandler`, `IAnalyticsErasableSink`; ClickHouse TTL, queue purge; Parquet by the lake's tools) | Done, 5 unit tests and 2 ClickHouse integration tests pass | `185034f` |
| Lineage and OpenLineage emitter (verify against Marquez) | Open (task #5) | |

## Test status

Last full run of the analytics suites (both frameworks where applicable):

| Suite | Result |
|---|---|
| Modulus.Analytics.Tests | 56 pass |
| Modulus.Analytics.EntityFrameworkCore.Tests | 85 pass |
| Modulus.Analytics.Management.Tests | 33 pass |
| Modulus.Analytics.ClickHouse.Tests | 23 pass |
| Modulus.Analytics.Parquet.Tests | 25 pass |
| Integration: ClickHouse (real server) | 15 pass (after the erasure change) |
| Integration: EF (real PostgreSQL 16) | 8 pass (last run before the Phase 4 changes; not re-run since) |

`dotnet format --verify-no-changes` is clean for the analytics projects. Each changed project builds with 0 warnings.

## Not yet verified

- Full-solution build (`dotnet build modulus.slnx`) and the full unit suite have not been run. Only the analytics projects have.
- Integration suites were not re-run after the Phase 4 permission and audit changes.

## Next steps

1. Phase 4 OpenLineage emitter, checked against a real Marquez container (task #5).
2. Phase 4 data-quality findings beyond counts.
3. Phase 5, then Phase 6.

## Decisions to know about

- Commit trailers: no model name in commit messages (repo rule). The `Claude-Session` trailer is kept.
- Denied requests are not audited by the analytics management API; the framework's access-decision audit covers them.
- Audit entries go to the host chain (`TenantId` null), because one request can span tenants.
