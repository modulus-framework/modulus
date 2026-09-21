-- Migration: Add_inbox_messages
-- Description: Adds the transactional inbox table matching the current Modulus inbox model.

CREATE TABLE IF NOT EXISTS administration.inbox_messages (
    id UUID NOT NULL,
    handler_name VARCHAR(500) NOT NULL DEFAULT '',
    message_type VARCHAR(500) NOT NULL,
    module_name VARCHAR(100) NOT NULL,
    payload TEXT NOT NULL,
    tenant_id UUID NOT NULL,
    received_at TIMESTAMPTZ NOT NULL,
    processed_at TIMESTAMPTZ NULL,
    status VARCHAR(20) NOT NULL,
    error TEXT NULL,
    retry_count INTEGER NOT NULL,
    correlation_id TEXT NULL,
    claimed_at TIMESTAMPTZ NULL,
    CONSTRAINT pk_inbox_messages PRIMARY KEY (id, handler_name)
);

CREATE INDEX IF NOT EXISTS ix_inbox_messages_status_received_at
    ON administration.inbox_messages (status, received_at);
