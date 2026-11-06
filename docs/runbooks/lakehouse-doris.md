# Lakehouse runbook: Apache Doris

Operator procedure for `Modulus.Analytics.Doris`. The sink loads each delivered batch with Stream Load into a unique-key table. The tables are created by
the operator, because bucket and replica counts are a cluster decision.

## 1. What the sink does

- It sends a batch as one Stream Load (`PUT http://<fe>:8030/api/<database>/<table>/_stream_load`, JSON, one object per record). The frontend redirects
  the load to a backend; the sink follows the redirect with the same request and the same credentials.
- The load label is a hash of the batch's records (tenant, record id and revision). A batch sent again carries the same label, and Doris answers
  `Label Already Exists` with the earlier job's result, which the sink accepts. A batch is therefore not loaded twice when a delivery is retried.
- A row is replaced by a later revision of the same record, through the table's sequence column (`revision`).
- A server error (HTTP 5xx) and a timeout are transient: the delivery retries. A refused load (for example a missing partition or a missing table) is
  permanent and goes to the dead letters with Doris's message.

## 2. Tables

Doris requires the key columns first in a unique-key table. For a dataset `orders` version 1 with the fields `order_id` (Int64), `total` (Decimal),
`placed_on` (Date, nullable), `placed_at` (Timestamp) and `paid` (Boolean):

```sql
CREATE TABLE IF NOT EXISTS analytics.app_orders_v1 (
  tenant_id   VARCHAR(36)   NOT NULL,
  event_id    VARCHAR(36)   NOT NULL,
  revision    BIGINT        NOT NULL,
  occurred_at DATETIME(6)   NOT NULL,
  ingested_at DATETIME(6)   NOT NULL,
  order_id    BIGINT,
  total       DECIMAL(38,9),
  placed_on   DATE,
  placed_at   DATETIME(6),
  paid        BOOLEAN
)
UNIQUE KEY(tenant_id, event_id)
DISTRIBUTED BY HASH(tenant_id) BUCKETS 8
PROPERTIES (
  "replication_num" = "3",
  "function_column.sequence_col" = "revision"
);
```

The column types follow the dataset: `String` becomes `STRING`, `Int64` `BIGINT`, `Decimal` `DECIMAL(38,9)`, `Double` `DOUBLE`, `Boolean` `BOOLEAN`,
`Timestamp` `DATETIME(6)`, `Date` `DATE`, `Guid` `VARCHAR(36)`. Nullable fields omit `NOT NULL`. The table name is the prefix, the dataset name and
`_v<version>`. A new dataset version is a new table: create it before the first delivery.

Set `replication_num` for the cluster (3 in production, 1 for a single-node test). Bucket count follows the data volume.

## 3. Application settings

```json
{
  "Analytics": {
    "Doris": {
      "FeHttpUrl": "http://doris-fe:8030",
      "Database": "analytics",
      "TablePrefix": "app_",
      "Username": "doris_loader",
      "Password": "from-a-secret-store",
      "Timeout": "00:01:00"
    }
  }
}
```

Register the sink: `builder.Services.AddModulusAnalytics(a => a.AddDoris(builder.Configuration));`. The loader user needs the LOAD privilege on the
database only. The FE address must be the frontend's HTTP port, not the MySQL port.

## 4. Checks

- A failed batch appears in the dead letters with Doris's message in `LastError`. Replay it after the cause is fixed (`POST /analytics/dead-letters/replay`).
- A batch that Doris reports as `Publish Timeout` is treated as loaded: the data is committed and only visibility is delayed. Check the table if a count
  looks short.
- Reconciliation does not count Doris tables (the sink cannot count them yet). Count the table with SQL: `SELECT COUNT(*) FROM analytics.app_orders_v1`
  against the source, for a single tenant.

## 5. What is verified

The sink is unit-tested against a fake Doris frontend and backend over HTTP: the redirect is followed with the body and the credentials, the label is
stable for a resent batch, a `Label Already Exists` answer is accepted, a refused load is permanent with its reason, a server error is transient, and the
row format matches the DDL in section 2. A live Doris cluster was not run in this environment (the image is several gigabytes), so run the DDL and one
delivery against your cluster before you rely on it.
