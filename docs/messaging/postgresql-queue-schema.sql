-- Modulus.Messaging PostgreSQL transport: queue table, sequence and indexes for the default
-- schema ("public") and table ("messaging_queue"). The transport runs this same script on start
-- unless CreateSchema is false. For another schema or table, generate the script with
-- PostgreSqlTransport.SchemaScript(options). The application role must not own this table.
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
    sequence bigint NOT NULL DEFAULT nextval('"public"."messaging_queue_seq"')
);
CREATE INDEX IF NOT EXISTS "messaging_queue_ready_idx" ON "public"."messaging_queue" (queue, visible_at, id);
CREATE INDEX IF NOT EXISTS "messaging_queue_claim_idx" ON "public"."messaging_queue" (queue, priority DESC, id);
CREATE INDEX IF NOT EXISTS "messaging_queue_schedule_idx" ON "public"."messaging_queue" (schedule_id) WHERE schedule_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS "messaging_queue_partition_idx" ON "public"."messaging_queue" (queue, partition_key, sequence) WHERE partition_key IS NOT NULL;
