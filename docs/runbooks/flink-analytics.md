# Apache Flink on the analytics records

Modulus does not ship Flink code. Flink is a separate, JVM-based stream processor, so the framework's part is the **contract**:
the analytics messaging sink publishes every record on Kafka, and a Flink job reads that topic. This runbook describes that job
and what is checked automatically.

## What is in the repository

| Piece | Where | Checked by |
|---|---|---|
| Publisher: each analytics record as `AnalyticsRecordPublished` on the Kafka bus | `src/analytics/Modulus.Analytics.Messaging` (`AddMessaging()`) | `MessagingAnalyticsSinkTests` |
| The Flink SQL job (Kafka source, JSON extraction, latest-revision dedup, upsert output) | `deploy/flink/analytics-records.sql` | not run against a Flink cluster (see below) |
| The wire contract the job depends on (topic name, envelope fields, payload fields) | `tests/unit/Modulus.Analytics.Messaging.Tests/AnalyticsRecordWireContractTests.cs` | runs in `dotnet test` (unit) |

The unit test reads the SQL file and fails if its topic name differs from the topic the framework derives, or if the envelope or
payload loses a field the SQL extracts. A change on either side is caught before the job reads nothing.

## Message format

- **Topic:** `{TopicPrefix}.{wire name}`. With the default prefix `modulus`:
  `modulus.analytics-messaging.analytics-record-published.v1`.
- **Value:** an `IntegrationEventEnvelope` as camelCase JSON: `eventId`, `typeName`, `occurredAt`, `payload`, `tenantId`
  (omitted when null), `correlationId` (omitted when null), trace fields.
- **Payload:** the event itself, as a JSON string inside `payload`: `tenantId`, `dataset`, `schemaVersion`, `revision`,
  `valuesJson` (the masked field values as a JSON object, also a string), `eventId`, `occurredAt`.
- **Delivery is at least once.** A failed batch is sent again in full, so consumers deduplicate on
  `(tenantId, dataset, eventId)` and keep the highest `revision`. The SQL job does this.

## Running the job

1. Run Kafka and bring up the Modulus application with the Kafka bus (`AddKafkaEventBus`) and the messaging sink
   (`AddAnalytics(...).AddMessaging()`). Records appear on the topic once the analytics queue delivers them.
2. Start a Flink cluster with the Kafka SQL connector on its classpath (`flink-sql-connector-kafka`, matching the Flink version).
3. Edit `deploy/flink/analytics-records.sql`: set `properties.bootstrap.servers` (the placeholder is `kafka:9092`) and
   `properties.group.id`.
4. Submit it: `sql-client.sh -f deploy/flink/analytics-records.sql`.
5. Read the output topic `analytics.records.latest` (upsert, keyed by `tenant_id, dataset, event_id`), or point a downstream
   sink (ClickHouse, Doris, the lake loader) at it.

## Operating notes

- The `payload` is double-encoded (a JSON string inside the envelope). `JSON_VALUE` in the SQL handles it; a custom consumer must
  parse twice.
- `scan.startup.mode = earliest-offset` replays the whole topic on the first run. Use `latest-offset` for a live-only consumer.
- The `ROW_NUMBER` dedup keeps state for every key seen. For a long-running job, bound the state (Flink state TTL) or move the
  dedup into the sink's upsert key.
- The Modulus side holds no Flink state; a Flink outage only delays consumers, because Kafka keeps the records.

## Not verified here

The SQL has not been executed against a live Flink cluster in this repository, because that needs a Flink runtime and the Kafka
connector jar, which the framework does not bundle. The contract test verifies the inputs the SQL depends on, not the SQL
itself. Run the job once in a test environment before relying on it.
