# Lakehouse runbook: change data capture with Debezium

Operator procedure for `Modulus.Analytics.Debezium`: how a source table's inserts, updates and hard deletes reach the warehouse. Read it with
`docs/DOCUMENTATION.md` (Analytics Integration Layer, Phase 6).

## 1. What the reader does and does not do

- It maps each Debezium change event of a mapped table to an analytics record. Inserts, updates and snapshot reads (`c`, `u`, `r`) carry the row
  `after`; a hard delete (`d`) carries the row `before` and sets the dataset's Boolean delete marker to `true`. Truncates and messages are ignored.
- The record id is the mapped key column, so one source row is one warehouse row. The revision is the source log position (`source.lsn`), or the source
  timestamp when there is no position, so a later change wins.
- It reads events. It does not consume Kafka topics itself: the host's consumer calls `DebeziumIngestor.IngestAsync` with the messages it reads.
  Events that cannot be read come back as problems and do not stop the batch.
- It has no schema registry. The connector must write plain JSON values (section 2).

## 2. Connector settings (Kafka Connect, Debezium PostgreSQL)

These settings are required. Other values are the operator's choice.

| Setting | Value | Why |
|---|---|---|
| `connector.class` | `io.debezium.connector.postgresql.PostgresConnector` | The source. Use the MySQL or SQL Server connector the same way. |
| `plugin.name` | `pgoutput` | Built into PostgreSQL. |
| `topic.prefix` | `app` | Topics are named `app.<schema>.<table>`. |
| `table.include.list` | `public.orders` | Only the tables that have a mapping. |
| `value.converter` | `org.apache.kafka.connect.json.JsonConverter` | Plain JSON. |
| `value.converter.schemas.enable` | `false` | The reader reads the envelope without schemas. |
| `decimal.handling.mode` | `string` | A decimal arrives as text. The reader refuses a binary decimal rather than read it wrongly. |
| `time.precision.mode` | `adaptive_time_microseconds` | Timestamps arrive as microseconds since the Unix epoch, which the reader expects. |

A minimal connector definition, to adapt (`POST` it to the Kafka Connect REST API, `/connectors`):

```json
{
  "name": "app-orders-cdc",
  "config": {
    "connector.class": "io.debezium.connector.postgresql.PostgresConnector",
    "database.hostname": "app-db",
    "database.port": "5432",
    "database.user": "debezium",
    "database.password": "from-a-secret-store",
    "database.dbname": "app",
    "topic.prefix": "app",
    "plugin.name": "pgoutput",
    "slot.name": "modulus_orders",
    "publication.autocreate.mode": "filtered",
    "table.include.list": "public.orders",
    "value.converter": "org.apache.kafka.connect.json.JsonConverter",
    "value.converter.schemas.enable": "false",
    "decimal.handling.mode": "string",
    "time.precision.mode": "adaptive_time_microseconds"
  }
}
```

The database user needs the replication privilege and read access to the mapped tables. Keep the password in the Kafka Connect secret provider, not in
the file.

## 3. Mapping a table in the application

Declare the dataset with its field names equal to the source columns, and a Boolean field for the delete marker:

```csharp
var orders = new AnalyticsDatasetDescriptor("orders_cdc", 1, "sales",
[
    new("id", AnalyticsFieldType.Int64),
    new("tenant_id", AnalyticsFieldType.Guid),
    new("total", AnalyticsFieldType.Decimal),
    new("placed_at", AnalyticsFieldType.Timestamp, IsNullable: true),
    new("deleted", AnalyticsFieldType.Boolean),
]);

services.AddModulusDebezium([new DebeziumTableMapping("orders", orders, KeyColumn: "id", DeletedField: "deleted")]);
```

The mapping is checked when the reader is built: an invalid dataset, a missing table name, or a delete marker that is not a Boolean field stops the
application at startup.

## 4. Deletes in the warehouse

A delete is a record with `deleted = true`, not a removed row. Queries that show current data filter on it:

```sql
SELECT * FROM app_orders_cdc_v1 FINAL WHERE deleted = 0;
```

Reconciliation counts the warehouse's distinct rows, deleted ones included, so a table with deletes reconciles as *extra* until the consumer filters
them out. Compare with `WHERE deleted = 0` when you reconcile a CDC dataset by hand.

## 5. Checks

- Lag: the warehouse can be as far behind as the connector. Watch the connector's lag metric and the dataset's freshness
  (`GET /analytics/freshness`).
- Problems: the ingestor's returned problems are the events that were not stored. Log them. A steady stream of the same problem is a mapping or a
  connector setting (section 2), not a transient fault.
- Snapshot: an initial snapshot emits `r` events for every row. It is a full copy: plan the warehouse load for it.

## 6. What is verified

The reader and the ingestor are unit-tested against Debezium's documented envelope (inserts, updates, snapshot reads, deletes, truncates, unknown tables,
the schemas-enabled payload, binary decimals and bad events). A live Debezium connector and a Kafka broker were not run in this environment, so the
connector definition in section 2 must be checked against the Debezium release you deploy before you rely on it.
