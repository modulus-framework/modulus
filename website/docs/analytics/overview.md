---
sidebar_position: 1
title: Analytics overview
description: Publish module data to analytics stores as versioned datasets, with masking, consent and governance built in.
---

# Analytics integration layer

The analytics layer sends data from modules to analytics stores such as ClickHouse, Parquet files in a lake, or Apache Doris. Each module publishes **datasets**: versioned, documented tables whose fields are the contract. The layer keeps the data classified and masked, and it can backfill history and check quality.

The layer is opt-in. Most applications need `Modulus.Analytics`, one sink, and optionally the management API.

## Concepts

| Concept | What it is |
|---------|-----------|
| **Dataset** | A named table with a version, an owner, a freshness target, a retention period and a list of fields. `AnalyticsDatasetDescriptor` holds it. |
| **Field** | A column with a type (`String`, `Decimal`, `Timestamp`, `Guid`, ...), a classification and a masking action. |
| **Capture** | Turns an integration event into a record of a dataset version. |
| **Source** | Reads a dataset from the operational store, for backfills and incremental sync. |
| **Sink** | Writes records to a store. Several sinks may run; each receives every dataset. |
| **Delivery queue** | A durable queue between capture and sinks, so a slow store does not block the module. |

A breaking change to a dataset's fields is a new version, never an edit, the same rule integration events follow.

## Registration

```csharp
builder.Services.AddModulusAnalytics(analytics => analytics
    .AddDataset(new AnalyticsDatasetDescriptor(
        Name: "sales_orders",
        Version: 1,
        Owner: "Sales",
        Fields:
        [
            new AnalyticsFieldDescriptor("order_id", AnalyticsFieldType.Guid),
            new AnalyticsFieldDescriptor("total", AnalyticsFieldType.Decimal),
            new AnalyticsFieldDescriptor("placed_at", AnalyticsFieldType.Timestamp),
            new AnalyticsFieldDescriptor("customer_email", AnalyticsFieldType.String,
                Masking: AnalyticsMaskingAction.Hash),
        ],
        Description: "One row per placed order",
        FreshnessSla: TimeSpan.FromMinutes(15),
        RetentionDays: 730))
    .MapEvent<OrderPlacedEvent>("sales_orders", version: 1, e => new Dictionary<string, object?>
    {
        ["order_id"] = e.OrderId,
        ["total"] = e.Total,
        ["placed_at"] = e.PlacedAt,
        ["customer_email"] = e.CustomerEmail,
    })
    .AddClickHouse(builder.Configuration));
```

- `MapEvent` builds the record from the event. The record id is the event id and the tenant is the current tenant, so a redelivered event is queued only once. Return only fields the event may share; masking is applied afterwards.
- `AddSource<TSource>()` registers a backfill source for the dataset.
- `AddUpcaster<TUpcaster>()` moves old record versions to a newer version.

## Masking and classification

A field's masking action decides what leaves the system:

| Action | Effect |
|--------|--------|
| `Keep` | The value is stored as is. |
| `Hash` | The value is replaced by a keyed hash. It can still be grouped and joined, but not read. |
| `Drop` | The value is not stored. |

Each field has a default action derived from its classification. Set `Masking` to override it.

## Operating it

Each sink is configured under its own section, such as `Analytics:ClickHouse` or `Analytics:Parquet`. Circuit breaking is configured under `Analytics:CircuitBreaker`.

The management API is opt-in:

```csharp
builder.Services.AddModulusAnalyticsManagement();
...
app.MapModulusAnalyticsManagement("/analytics");
```

It exposes the status, datasets and their schemas, freshness, dead letters (with replay and purge), quality checks, reconciliations and backfills. Its permissions are `analytics:manage:{dataset}`.

## Command-line support

```bash
modulus add-analytics               # writes the sink settings and a local docker-compose file
modulus add-analytics --no-compose  # skip the local ClickHouse and RabbitMQ compose file
```

The command prints the registration lines to add to the API host. Set `Analytics:ClickHouse:ConnectionString` from a secret store in production; the generated file contains a placeholder only.

## See also

- [Sinks](./sinks): ClickHouse, Parquet, Doris and the messaging sink
- [Governance](./governance): consent, plan gating, quality, retention, catalogs and lineage
