-- Migration: Initial_schema
-- Author: Meetup
-- Created: 2026-09-15
-- Description: Initial schema for the Meetings module (dbsh SQL-first; EF only maps it).

CREATE SCHEMA IF NOT EXISTS meetings;

CREATE TABLE IF NOT EXISTS meetings.groups (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT NOT NULL,
    city TEXT NOT NULL,
    country_code TEXT NOT NULL,
    creator_login TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    payment_valid_until TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS meetings.group_members (
    id UUID PRIMARY KEY,
    group_id UUID NOT NULL,
    login TEXT NOT NULL,
    role INTEGER NOT NULL,
    joined_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS meetings.meetings (
    id UUID PRIMARY KEY,
    group_id UUID NOT NULL,
    title TEXT NOT NULL,
    description TEXT NOT NULL,
    start_utc TIMESTAMPTZ NOT NULL,
    end_utc TIMESTAMPTZ NOT NULL,
    attendees_limit INTEGER NULL,
    guests_limit INTEGER NOT NULL,
    event_fee NUMERIC NOT NULL,
    event_fee_currency TEXT NOT NULL,
    creator_login TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS meetings.attendees (
    id UUID PRIMARY KEY,
    meeting_id UUID NOT NULL,
    login TEXT NOT NULL,
    guests_count INTEGER NOT NULL,
    is_host BOOLEAN NOT NULL,
    status INTEGER NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS meetings.comments (
    id UUID PRIMARY KEY,
    meeting_id UUID NOT NULL,
    author_login TEXT NOT NULL,
    text TEXT NOT NULL,
    reply_to_id UUID NULL,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS meetings.entity_changes (
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

CREATE TABLE IF NOT EXISTS meetings.outbox_messages (
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

CREATE INDEX IF NOT EXISTS ix_entity_changes_correlation_id ON meetings.entity_changes (correlation_id);
CREATE INDEX IF NOT EXISTS ix_entity_changes_entity_key ON meetings.entity_changes (entity_name, entity_key, changed_at);
CREATE INDEX IF NOT EXISTS ix_entity_changes_tenant_id ON meetings.entity_changes (tenant_id, changed_at);
CREATE INDEX IF NOT EXISTS ix_outbox_messages_pending ON meetings.outbox_messages (processed_at, created_at);
CREATE INDEX IF NOT EXISTS ix_outbox_messages_claim ON meetings.outbox_messages (processed_at, locked_until, retry_count);
CREATE INDEX IF NOT EXISTS ix_outbox_messages_tenant_id ON meetings.outbox_messages (tenant_id);
