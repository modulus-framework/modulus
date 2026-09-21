-- Migration: Initial_schema
-- Author: Meetup
-- Created: 2026-09-15
-- Description: Initial Payments schema (dbsh SQL-first; EF only maps it).

CREATE SCHEMA IF NOT EXISTS payments;

CREATE TABLE IF NOT EXISTS payments.subscriptions (
    id UUID PRIMARY KEY,
    payer_login TEXT NOT NULL,
    valid_from TIMESTAMPTZ NOT NULL,
    valid_to TIMESTAMPTZ NOT NULL,
    price NUMERIC NOT NULL,
    currency TEXT NOT NULL,
    purchased_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS payments.meeting_fee_payments (
    id UUID PRIMARY KEY,
    payer_login TEXT NOT NULL,
    meeting_id UUID NOT NULL,
    amount NUMERIC NOT NULL,
    currency TEXT NOT NULL,
    paid_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS payments.entity_changes (
    id UUID PRIMARY KEY,
    entity_name VARCHAR(255) NOT NULL,
    entity_key VARCHAR(500) NOT NULL,
    tenant_id UUID NOT NULL,
    property_name VARCHAR(255) NOT NULL,
    original_value TEXT NULL,
    new_value TEXT NULL,
    changed_by VARCHAR(255) NOT NULL,
    changed_at TIMESTAMPTZ NOT NULL,
    correlation_id TEXT NULL,
    operation VARCHAR(20) NOT NULL
);

CREATE TABLE IF NOT EXISTS payments.outbox_messages (
    id UUID PRIMARY KEY,
    message_type VARCHAR(500) NOT NULL,
    payload TEXT NOT NULL,
    tenant_id UUID NOT NULL,
    module_name VARCHAR(100) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    processed_at TIMESTAMPTZ NULL,
    locked_by TEXT NULL,
    locked_until TIMESTAMPTZ NULL,
    next_attempt_at TIMESTAMPTZ NULL,
    retry_count INTEGER NOT NULL,
    error TEXT NULL,
    correlation_id TEXT NULL,
    causation_id TEXT NULL,
    trace_parent TEXT NULL,
    trace_state TEXT NULL,
    schema_version INTEGER NULL
);

CREATE INDEX IF NOT EXISTS ix_entity_changes_correlation_id ON payments.entity_changes (correlation_id);
CREATE INDEX IF NOT EXISTS ix_entity_changes_entity_key ON payments.entity_changes (entity_name, entity_key, changed_at);
CREATE INDEX IF NOT EXISTS ix_entity_changes_tenant_id ON payments.entity_changes (tenant_id, changed_at);
CREATE INDEX IF NOT EXISTS ix_outbox_messages_pending ON payments.outbox_messages (processed_at, created_at);
CREATE INDEX IF NOT EXISTS ix_outbox_messages_claim ON payments.outbox_messages (processed_at, locked_until, retry_count);
CREATE INDEX IF NOT EXISTS ix_outbox_messages_tenant_id ON payments.outbox_messages (tenant_id);
