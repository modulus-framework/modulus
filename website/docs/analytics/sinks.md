---
sidebar_position: 2
title: Analytics sinks
description: The stores analytics records can be written to, and how each one is configured.
---

# Analytics sinks

A sink receives every record of every dataset. Register one with its extension on `AnalyticsBuilder`. The sink's options bind from its own configuration section.

| Sink | Registration | Section | Use it for |
|------|--------------|---------|------------|
| **ClickHouse** | `AddClickHouse(configuration)` | `Analytics:ClickHouse` | Fast analytical queries over live data |
| **Parquet** | `AddParquet(configuration)` | `Analytics:Parquet` | Files in a data lake, read by DuckDB, Spark or other tools |
| **Apache Doris** | `AddDoris(configuration)` | `Analytics:Doris` | A real-time OLAP store with Stream Load |
| **Messaging** | `AddMessaging()` | none | Republishing records on the module bus |

## ClickHouse

```csharp
analytics.AddClickHouse(builder.Configuration, o => o.TablePrefix = "app_");
```

```json
"Analytics": {
  "ClickHouse": {
    "ConnectionString": "Host=clickhouse;Port=9000;Database=analytics"
  }
}
```

`TablePrefix` is added to every table name. Erasure for a tenant is performed as a TTL and queue purge, so deleting a company removes its analytics rows.

## Parquet

```csharp
analytics.AddParquet(builder.Configuration);
```

```json
"Analytics": { "Parquet": { "RootPath": "/data/analytics" } }
```

Each dataset is written as Parquet files under `RootPath`. Erasure is performed by the lake's own tools.

## Apache Doris

```csharp
analytics.AddDoris(builder.Configuration);
```

`Analytics:Doris:FeHttpUrl` names the Doris frontend that accepts Stream Load requests.

## Messaging

```csharp
analytics.AddMessaging();
```

The messaging sink publishes each captured record as `AnalyticsRecordPublished` on the module bus. Other modules, or a broker, can subscribe to it.

## Backfill and incremental sync

A sink does not read history by itself. Register a source for a dataset, and it can backfill from the operational store:

```csharp
analytics.AddSource<OrderAnalyticsSource>();
```

The EF Core package provides entity sources. Incremental entity sources read by modification time, so only changes are read after the first run. Hard deletes are not seen; reconciliation reports the difference.

## See also

- [Overview](./overview): datasets, fields and capture
- [Governance](./governance): consent, quality and retention
