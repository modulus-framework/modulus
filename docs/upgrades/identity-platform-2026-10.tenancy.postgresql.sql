-- Upgrade for existing PostgreSQL deployments, TENANCY database: the identity platform changes of 2026-10.
--
-- Run as the schema owner, never as an application role, once, before the new release starts:
--   psql "$TENANCY_CONNECTION" -v ON_ERROR_STOP=1 -f identity-platform-2026-10.tenancy.postgresql.sql
--
-- Adds the company lifecycle columns (Status, TrialEndsAt, MaxUsers) and the parent company (ParentId) to ModulusTenants and creates ModulusTenantDataStores and ModulusSettingValues.
-- Idempotent: a second run changes nothing. Existing companies start as Active with no trial end and no seat limit, which is
-- what they were before. Existing deployments only: it expects ModulusTenants to exist.

BEGIN;

-- 1. Company lifecycle. Status is the TenantStatus enum stored as its integer value (Trial 0, Active 1, Suspended 2, Closed 3),
--    the default for every existing row, matching how EF maps the enum without a converter.
ALTER TABLE IF EXISTS "ModulusTenants" ADD COLUMN IF NOT EXISTS "Status" integer NOT NULL DEFAULT 1;
ALTER TABLE IF EXISTS "ModulusTenants" ADD COLUMN IF NOT EXISTS "TrialEndsAt" timestamptz NULL;
ALTER TABLE IF EXISTS "ModulusTenants" ADD COLUMN IF NOT EXISTS "MaxUsers" integer NULL;
-- The parent company (null for a top-level company). A plain fact: access does not follow it.
ALTER TABLE IF EXISTS "ModulusTenants" ADD COLUMN IF NOT EXISTS "ParentId" uuid NULL;

-- 2. Tenant data stores: a pointer to a company's database secret, never a connection string.
CREATE TABLE IF NOT EXISTS "ModulusTenantDataStores" (
    "TenantId" uuid NOT NULL,
    "StoreKey" varchar(32) NOT NULL,
    "Isolation" varchar(32) NOT NULL,
    "ProviderKey" varchar(32) NOT NULL,
    "SecretPath" varchar(256) NOT NULL,
    "SecretVersion" integer NOT NULL DEFAULT 0,
    "Status" varchar(32) NOT NULL,
    CONSTRAINT "PK_ModulusTenantDataStores" PRIMARY KEY ("TenantId", "StoreKey")
);

-- 3. Setting values: one row per setting and scope (installation, company or person). ScopeKey is a non-null text column, so
--    (Name, ScopeKey) is unique at every scope, the installation's included, on every engine.
CREATE TABLE IF NOT EXISTS "ModulusSettingValues" (
    "Name" varchar(128) NOT NULL,
    "ScopeKey" varchar(80) NOT NULL,
    "TenantId" uuid NULL,
    "UserId" uuid NULL,
    "Value" varchar(4000) NOT NULL,
    CONSTRAINT "PK_ModulusSettingValues" PRIMARY KEY ("Name", "ScopeKey")
);

-- 4. Settings change counter: every settings write advances it, and the other nodes check it on a schedule, so a change reaches them
--    without Redis. The first write creates the row.
CREATE TABLE IF NOT EXISTS "ModulusSettingVersions" (
    "Name" varchar(64) NOT NULL,
    "Version" bigint NOT NULL,
    CONSTRAINT "PK_ModulusSettingVersions" PRIMARY KEY ("Name")
);

COMMIT;
