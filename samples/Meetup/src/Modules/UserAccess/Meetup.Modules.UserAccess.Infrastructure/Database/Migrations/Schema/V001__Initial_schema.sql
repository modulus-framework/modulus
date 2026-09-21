-- Migration: Initial_schema
-- Author: Meetup
-- Created: 2026-09-15
-- Description: Initial UserAccess schema (users + framework outbox and entity-change history) in schema useraccess.

CREATE SCHEMA IF NOT EXISTS useraccess;

CREATE TABLE IF NOT EXISTS useraccess.users (
    id UUID PRIMARY KEY,
    login TEXT NOT NULL,
    email TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    is_active BOOLEAN NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS useraccess.entity_changes (
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

CREATE TABLE IF NOT EXISTS useraccess.outbox_messages (
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
    ON useraccess.entity_changes (correlation_id);

CREATE INDEX IF NOT EXISTS ix_entity_changes_entity_key
    ON useraccess.entity_changes (entity_name, entity_key, changed_at);

CREATE INDEX IF NOT EXISTS ix_entity_changes_tenant_id
    ON useraccess.entity_changes (tenant_id, changed_at);

CREATE INDEX IF NOT EXISTS ix_outbox_messages_pending
    ON useraccess.outbox_messages (processed_at, created_at);

CREATE INDEX IF NOT EXISTS ix_outbox_messages_claim
    ON useraccess.outbox_messages (processed_at, locked_until, retry_count);

CREATE INDEX IF NOT EXISTS ix_outbox_messages_tenant_id
    ON useraccess.outbox_messages (tenant_id);
