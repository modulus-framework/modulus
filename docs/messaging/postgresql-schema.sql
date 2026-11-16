-- Modulus.Messaging: PostgreSQL schema of the queue table (FR-14, FR-21).
--
-- Run this before the first start when the application does not create the schema itself
-- (PostgreSqlTransportOptions.CreateSchema = false). It is the same script the transport runs
-- on start, for the default schema "public" and table "messaging_queue". A different schema
-- or table name needs the generated script: PostgreSqlTransport.SchemaScript(options).
--
-- Upgrading an existing table: every statement is idempotent (IF NOT EXISTS / ADD COLUMN IF NOT
-- EXISTS), so the script can be run again. It adds the tenant column and the claim index used by
-- the claim query (queue, priority DESC, id); on a large backlog, build the index concurrently
-- in a maintenance window instead, since CREATE INDEX holds a write lock on the table.
--
-- Checked by PostgreSqlSchemaScriptTests against PostgreSqlQueueSql.CreateSchema.

CREATE SEQUENCE IF NOT EXISTS "public"."messaging_queue_seq";
CREATE TABLE IF NOT EXISTS "public"."messaging_queue" (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    queue text NOT NULL,
    message_id uuid NOT NULL,
    message_type text NOT NULL,
    consumer_key text NULL,
    body text NOT NULL,
    headers jsonb NOT NULL,
    visible_at timestamptz NOT NULL DEFAULT now(),
    locked_until timestamptz NULL,
    lock_token uuid NULL,
    delivery_count integer NOT NULL DEFAULT 0,
    enqueued_at timestamptz NOT NULL DEFAULT now(),
    schedule_id uuid NULL,
    priority integer NOT NULL DEFAULT 0,
    partition_key text NULL,
    sequence bigint NOT NULL DEFAULT nextval('"public"."messaging_queue_seq"'),
    tenant_id text NULL
);
ALTER TABLE "public"."messaging_queue" ADD COLUMN IF NOT EXISTS tenant_id text NULL;
CREATE INDEX IF NOT EXISTS "messaging_queue_tenant_idx" ON "public"."messaging_queue" (tenant_id) WHERE tenant_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS "messaging_queue_ready_idx" ON "public"."messaging_queue" (queue, visible_at, id);
CREATE INDEX IF NOT EXISTS "messaging_queue_claim_idx" ON "public"."messaging_queue" (queue, priority DESC, id);
CREATE INDEX IF NOT EXISTS "messaging_queue_schedule_idx" ON "public"."messaging_queue" (schedule_id) WHERE schedule_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS "messaging_queue_partition_idx" ON "public"."messaging_queue" (queue, partition_key, sequence) WHERE partition_key IS NOT NULL;
