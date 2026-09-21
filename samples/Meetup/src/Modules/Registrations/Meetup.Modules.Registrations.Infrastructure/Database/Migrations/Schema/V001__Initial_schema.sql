-- Migration: Initial_schema
-- Author: Meetup
-- Created: 2026-09-15
-- Description: Initial schema for the Registrations module (registrations schema, snake_case).

CREATE SCHEMA IF NOT EXISTS registrations;

CREATE TABLE IF NOT EXISTS registrations.registrations (
    id UUID PRIMARY KEY,
    login TEXT NOT NULL,
    email TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    first_name TEXT NOT NULL,
    last_name TEXT NOT NULL,
    status INTEGER NOT NULL,
    registered_at TIMESTAMPTZ NOT NULL,
    confirmed_at TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS registrations.entity_changes (
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

CREATE TABLE IF NOT EXISTS registrations.outbox_messages (
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

CREATE INDEX IF NOT EXISTS ix_entity_changes_correlation_id
    ON registrations.entity_changes (correlation_id);

CREATE INDEX IF NOT EXISTS ix_entity_changes_entity_key
    ON registrations.entity_changes (entity_name, entity_key, changed_at);

CREATE INDEX IF NOT EXISTS ix_entity_changes_tenant_id
    ON registrations.entity_changes (tenant_id, changed_at);

CREATE INDEX IF NOT EXISTS ix_outbox_messages_pending
    ON registrations.outbox_messages (processed_at, created_at);

CREATE INDEX IF NOT EXISTS ix_outbox_messages_claim
    ON registrations.outbox_messages (processed_at, locked_until, retry_count);

CREATE INDEX IF NOT EXISTS ix_outbox_messages_tenant_id
    ON registrations.outbox_messages (tenant_id);
