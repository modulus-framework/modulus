-- Upgrade for existing PostgreSQL deployments, AUTHORIZATION database: the identity platform changes of 2026-10 (VER-2).
--
-- Run as the schema owner, never as an application role, once, before the new release starts:
--   psql "$AUTHORIZATION_CONNECTION" -v ON_ERROR_STOP=1 -v api_owner=MyApp.Api -f identity-platform-2026-10.authorization.postgresql.sql
--
-- api_owner is the owner of the permission catalog rows recorded so far: the API host's assembly name (PermissionCatalogOptions.Owner).
-- Idempotent: a second run changes nothing. Existing deployments only: it expects the catalog and grant tables to exist.

BEGIN;

-- 1. Permission catalog: one set of rows per host (authorization database).
ALTER TABLE IF EXISTS "ModulusPermissionCatalog" ADD COLUMN IF NOT EXISTS "Owner" varchar(128);
UPDATE "ModulusPermissionCatalog" SET "Owner" = :'api_owner' WHERE "Owner" IS NULL;
ALTER TABLE IF EXISTS "ModulusPermissionCatalog" ALTER COLUMN "Owner" SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.key_column_usage k
        JOIN information_schema.table_constraints c
          ON c.constraint_name = k.constraint_name AND c.table_name = k.table_name
        WHERE k.table_name = 'ModulusPermissionCatalog'
          AND c.constraint_type = 'PRIMARY KEY'
          AND k.column_name = 'Owner')
    THEN
        ALTER TABLE "ModulusPermissionCatalog" DROP CONSTRAINT IF EXISTS "PK_ModulusPermissionCatalog";
        ALTER TABLE "ModulusPermissionCatalog" ADD CONSTRAINT "PK_ModulusPermissionCatalog" PRIMARY KEY ("Owner", "Permission");
    END IF;
END $$;

-- 2. First-boot grant seed marker (authorization database). A database that already holds grants is marked as seeded, so an upgrade
--    never re-applies the seed over grants an administrator has changed. An empty database is seeded on its first boot.
CREATE TABLE IF NOT EXISTS "ModulusAuthorizationSeeds" (
    "Name" varchar(128) NOT NULL,
    "AppliedAtUtc" timestamptz NOT NULL,
    CONSTRAINT "PK_ModulusAuthorizationSeeds" PRIMARY KEY ("Name")
);
INSERT INTO "ModulusAuthorizationSeeds" ("Name", "AppliedAtUtc")
SELECT 'permission-grants', now()
WHERE EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'ModulusPermissionGrants')
  AND EXISTS (SELECT 1 FROM "ModulusPermissionGrants")
ON CONFLICT ("Name") DO NOTHING;



-- 3. Dimension values (scope codes per company). A code is unique within its dimension and company.
CREATE TABLE IF NOT EXISTS "ModulusDimensionValues" (
    "TenantId" uuid NOT NULL,
    "Id" uuid NOT NULL,
    "Dimension" varchar(64) NOT NULL,
    "Code" varchar(64) NOT NULL,
    "Label" varchar(128) NOT NULL,
    "ParentId" uuid NULL,
    CONSTRAINT "PK_ModulusDimensionValues" PRIMARY KEY ("TenantId", "Id")
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_ModulusDimensionValues_TenantId_Dimension_Code" ON "ModulusDimensionValues" ("TenantId", "Dimension", "Code");
CREATE INDEX IF NOT EXISTS "IX_ModulusDimensionValues_TenantId_ParentId" ON "ModulusDimensionValues" ("TenantId", "ParentId");

-- 4. Permission exceptions: the register of privileged-permission exceptions (who keeps a Critical permission, why, until when).
CREATE TABLE IF NOT EXISTS "ModulusPermissionExceptions" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Permission" varchar(200) NOT NULL,
    "HolderType" integer NOT NULL,
    "Holder" varchar(200) NOT NULL,
    "Justification" varchar(2000) NOT NULL,
    "CompensatingControl" varchar(2000) NULL,
    "BusinessOwner" varchar(200) NOT NULL,
    "RequestedBy" uuid NOT NULL,
    "ValidFrom" timestamptz NOT NULL,
    "ValidUntil" timestamptz NOT NULL,
    "ReviewDueOn" timestamptz NOT NULL,
    "CreatedAt" timestamptz NOT NULL,
    "ReviewedBy" uuid NULL,
    "ReviewedAt" timestamptz NULL,
    "RevokedBy" uuid NULL,
    "RevokedAt" timestamptz NULL,
    CONSTRAINT "PK_ModulusPermissionExceptions" PRIMARY KEY ("Id")
);
CREATE INDEX IF NOT EXISTS "IX_PermissionExceptions_Holder" ON "ModulusPermissionExceptions" ("TenantId", "Permission", "HolderType", "Holder");

COMMIT;
