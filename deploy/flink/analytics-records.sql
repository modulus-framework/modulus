-- Apache Flink SQL job: the analytics records that Modulus publishes on Kafka, deduplicated to the latest revision.
--
-- Reads the topic written by the Modulus analytics messaging sink (Modulus.Analytics.Messaging, AddMessaging()) through the Kafka
-- bus (AddKafkaEventBus, TopicPrefix "modulus"). The message value is an IntegrationEventEnvelope (camelCase JSON); its
-- `payload` is the AnalyticsRecordPublished event as a JSON string. A record can arrive more than once (a retried batch), so the
-- job keeps one row per (tenant, dataset, event) with the highest revision: the rule AnalyticsRecordPublished documents.
--
-- Run with the Flink SQL client against a cluster that has the flink-sql-connector-kafka jar (see docs/runbooks/flink-analytics.md).
-- Replace the broker address and the group id for your environment.

SET 'execution.checkpointing.interval' = '60 s';

CREATE TABLE analytics_envelopes (
  eventId       STRING,
  typeName      STRING,
  occurredAt    STRING,
  payload       STRING,
  tenantId      STRING,
  correlationId STRING
) WITH (
  'connector' = 'kafka',
  'topic' = 'modulus.analytics-messaging.analytics-record-published.v1',
  'properties.bootstrap.servers' = 'kafka:9092',
  'properties.group.id' = 'analytics-latest-records',
  'scan.startup.mode' = 'earliest-offset',
  'format' = 'json',
  'json.ignore-parse-errors' = 'true'
);

-- The event inside the envelope. JSON_VALUE reads the escaped payload string and returns its unescaped value.
CREATE VIEW analytics_records AS
SELECT
  JSON_VALUE(payload, '$.tenantId')                         AS tenant_id,
  JSON_VALUE(payload, '$.dataset')                          AS dataset,
  CAST(JSON_VALUE(payload, '$.schemaVersion') AS INT)       AS schema_version,
  CAST(JSON_VALUE(payload, '$.revision') AS BIGINT)         AS revision,
  JSON_VALUE(payload, '$.valuesJson')                       AS values_json,
  eventId                                                   AS event_id,
  occurredAt                                                AS occurred_at
FROM analytics_envelopes
WHERE typeName = 'analytics-messaging.analytics-record-published.v1';

-- Latest revision per (tenant, dataset, event). A later revision replaces an earlier row for the same key.
CREATE VIEW latest_analytics_records AS
SELECT tenant_id, dataset, schema_version, revision, values_json, event_id, occurred_at
FROM (
  SELECT *,
         ROW_NUMBER() OVER (PARTITION BY tenant_id, dataset, event_id ORDER BY revision DESC) AS rownum
  FROM analytics_records
)
WHERE rownum = 1;

-- Output: an upsert Kafka topic keyed by the dedup key, so a downstream table (ClickHouse, Doris, a lake loader) can read the
-- current state of each record.
CREATE TABLE analytics_records_latest (
  tenant_id      STRING,
  dataset        STRING,
  schema_version INT,
  revision       BIGINT,
  values_json    STRING,
  event_id       STRING,
  occurred_at    STRING,
  PRIMARY KEY (tenant_id, dataset, event_id) NOT ENFORCED
) WITH (
  'connector' = 'upsert-kafka',
  'topic' = 'analytics.records.latest',
  'properties.bootstrap.servers' = 'kafka:9092',
  'key.format' = 'json',
  'value.format' = 'json'
);

INSERT INTO analytics_records_latest
SELECT tenant_id, dataset, schema_version, revision, values_json, event_id, occurred_at
FROM latest_analytics_records;
