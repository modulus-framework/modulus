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

-- 5. The remaining authorization tables: every table of the authorization store that the steps above do not create (generated from the
--    EF model, 2026-10-23). Each statement is idempotent: an existing table or index is left as it is.
CREATE TABLE IF NOT EXISTS "ModulusAccessRequests" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Kind" integer NOT NULL,
    "RequesterId" uuid NOT NULL,
    "Permissions" character varying(4000) NOT NULL,
    "Reason" character varying(2000) NOT NULL,
    "Hours" integer NOT NULL,
    "Status" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "DecidedBy" uuid,
    "DecidedAt" timestamp with time zone,
    "Note" character varying(2000),
    "AccessEndsAt" timestamp with time zone,
    "ReviewedBy" uuid,
    "ReviewedAt" timestamp with time zone,
    CONSTRAINT "PK_ModulusAccessRequests" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ModulusApprovalAuthorities" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "HolderType" integer NOT NULL,
    "Holder" character varying(256) NOT NULL,
    "Permission" character varying(256) NOT NULL,
    "MaxAmount" numeric(18,4) NOT NULL,
    "Currency" character varying(8),
    "DocumentType" character varying(256),
    "OrgUnitId" uuid,
    "ValidFrom" timestamp with time zone,
    "ValidUntil" timestamp with time zone,
    "CreatedBy" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ModulusApprovalAuthorities" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ModulusApprovalChains" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Name" character varying(256) NOT NULL,
    "DocumentType" character varying(256) NOT NULL,
    "MinAmount" numeric(18,4),
    "MaxAmount" numeric(18,4),
    "Currency" character varying(8),
    "Steps" text NOT NULL,
    "IsActive" boolean NOT NULL,
    CONSTRAINT "PK_ModulusApprovalChains" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ModulusApprovalRequests" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "ChainId" uuid NOT NULL,
    "DocumentType" character varying(256) NOT NULL,
    "DocumentId" character varying(256) NOT NULL,
    "Amount" numeric(18,4) NOT NULL,
    "Currency" character varying(8),
    "OrgUnitId" uuid,
    "RequestedBy" uuid NOT NULL,
    "Summary" character varying(1000),
    "Steps" text NOT NULL,
    "CurrentStep" integer NOT NULL,
    "Status" integer NOT NULL,
    "Decisions" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "StepStartedAt" timestamp with time zone NOT NULL,
    "Escalated" boolean NOT NULL,
    "Version" bigint NOT NULL,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_ModulusApprovalRequests" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ModulusAssignments" (
    "TenantId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "AssignmentType" character varying(128) NOT NULL,
    "TargetId" uuid NOT NULL,
    "ValidFrom" timestamp with time zone,
    "ValidUntil" timestamp with time zone,
    CONSTRAINT "PK_ModulusAssignments" PRIMARY KEY ("TenantId", "UserId", "AssignmentType", "TargetId")
);

CREATE TABLE IF NOT EXISTS "ModulusAuthorizationAuditOutbox" (
    "Id" uuid NOT NULL,
    "MessageType" character varying(256) NOT NULL,
    "Payload" text NOT NULL,
    "TenantId" uuid NOT NULL,
    "ModuleName" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ProcessedAt" timestamp with time zone,
    "LockedBy" text,
    "LockedUntil" timestamp with time zone,
    "NextAttemptAt" timestamp with time zone,
    "RetryCount" integer NOT NULL,
    "Error" text,
    "CorrelationId" text,
    "CausationId" text,
    "TraceParent" text,
    "TraceState" text,
    "SchemaVersion" integer,
    CONSTRAINT "PK_ModulusAuthorizationAuditOutbox" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ModulusCompanyProfiles" (
    "TenantId" uuid NOT NULL,
    "LegalName" character varying(256) NOT NULL,
    "TradeName" character varying(256),
    "RegistrationNumber" character varying(64),
    "TaxId" character varying(64),
    "Address" character varying(1000),
    "Country" character varying(2),
    "Currency" character varying(3),
    "FiscalYearStartMonth" integer NOT NULL,
    "TimeZone" character varying(64),
    "Language" character varying(16),
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ModulusCompanyProfiles" PRIMARY KEY ("TenantId")
);

CREATE TABLE IF NOT EXISTS "ModulusDataQualityFindings" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ResourceType" text NOT NULL,
    "RecordId" text,
    "Action" text NOT NULL,
    "MissingAttributes" text NOT NULL,
    CONSTRAINT "PK_ModulusDataQualityFindings" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ModulusDelegations" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "FromUserId" uuid NOT NULL,
    "FromRolesJson" text NOT NULL,
    "ToUserId" uuid NOT NULL,
    "PermissionsJson" text NOT NULL,
    "NotBefore" timestamp with time zone NOT NULL,
    "NotAfter" timestamp with time zone NOT NULL,
    "Revoked" boolean NOT NULL,
    CONSTRAINT "PK_ModulusDelegations" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ModulusFeatureOverrides" (
    "TenantId" uuid NOT NULL,
    "Feature" character varying(256) NOT NULL,
    "Enabled" boolean NOT NULL,
    CONSTRAINT "PK_ModulusFeatureOverrides" PRIMARY KEY ("TenantId", "Feature")
);

CREATE TABLE IF NOT EXISTS "ModulusOrgPlacements" (
    "TenantId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "OrgUnitId" uuid NOT NULL,
    "Mode" integer NOT NULL,
    CONSTRAINT "PK_ModulusOrgPlacements" PRIMARY KEY ("TenantId", "UserId", "OrgUnitId")
);

CREATE TABLE IF NOT EXISTS "ModulusOrgUnitParents" (
    "TenantId" uuid NOT NULL,
    "ChildId" uuid NOT NULL,
    "ParentId" uuid NOT NULL,
    CONSTRAINT "PK_ModulusOrgUnitParents" PRIMARY KEY ("TenantId", "ChildId", "ParentId")
);

CREATE TABLE IF NOT EXISTS "ModulusOrgUnitProfiles" (
    "TenantId" uuid NOT NULL,
    "UnitId" uuid NOT NULL,
    "Code" character varying(64) NOT NULL,
    "NormalizedCode" character varying(64) NOT NULL,
    "Name" character varying(256) NOT NULL,
    "Kind" character varying(64) NOT NULL,
    "IsClosed" boolean NOT NULL,
    "ManagerUserId" uuid,
    "ClosedAt" timestamp with time zone,
    "UpdatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ModulusOrgUnitProfiles" PRIMARY KEY ("TenantId", "UnitId")
);

CREATE TABLE IF NOT EXISTS "ModulusOrgUnits" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    CONSTRAINT "PK_ModulusOrgUnits" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ModulusPartyCeilings" (
    "TenantId" uuid NOT NULL,
    "Kind" character varying(64) NOT NULL,
    "Permission" character varying(256) NOT NULL,
    CONSTRAINT "PK_ModulusPartyCeilings" PRIMARY KEY ("TenantId", "Kind", "Permission")
);

CREATE TABLE IF NOT EXISTS "ModulusPartyLinks" (
    "TenantId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Kind" character varying(64) NOT NULL,
    "PartyId" uuid NOT NULL,
    "CreatedBy" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ModulusPartyLinks" PRIMARY KEY ("TenantId", "UserId")
);

CREATE TABLE IF NOT EXISTS "ModulusPlanFeatures" (
    "Plan" character varying(128) NOT NULL,
    "Feature" character varying(256) NOT NULL,
    CONSTRAINT "PK_ModulusPlanFeatures" PRIMARY KEY ("Plan", "Feature")
);

CREATE TABLE IF NOT EXISTS "ModulusPositionAssignments" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "PositionId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "ValidFrom" timestamp with time zone NOT NULL,
    "ValidUntil" timestamp with time zone,
    "CreatedBy" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ModulusPositionAssignments" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ModulusPositions" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Code" character varying(100) NOT NULL,
    "NormalizedCode" character varying(100) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "OrgUnitId" uuid,
    "Roles" character varying(4000) NOT NULL,
    "IsActive" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ModulusPositions" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ModulusRecertificationCampaigns" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Name" text NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "CreatedBy" uuid NOT NULL,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_ModulusRecertificationCampaigns" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ModulusRecordShares" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "ResourceType" character varying(256) NOT NULL,
    "ResourceId" character varying(256) NOT NULL,
    "GranteeUserId" uuid NOT NULL,
    "SharedByUserId" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "RevokedAt" timestamp with time zone,
    CONSTRAINT "PK_ModulusRecordShares" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ModulusRoleInclusions" (
    "TenantId" uuid NOT NULL,
    "NormalizedRole" character varying(256) NOT NULL,
    "NormalizedIncludes" character varying(256) NOT NULL,
    "Role" character varying(256) NOT NULL,
    "Includes" character varying(256) NOT NULL,
    "CreatedBy" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ModulusRoleInclusions" PRIMARY KEY ("TenantId", "NormalizedRole", "NormalizedIncludes")
);

CREATE TABLE IF NOT EXISTS "ModulusScopedGrants" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "HolderType" integer NOT NULL,
    "Holder" character varying(256) NOT NULL,
    "Permission" character varying(256) NOT NULL,
    "Type" integer NOT NULL,
    "Scope" character varying(300) NOT NULL,
    "ValidFrom" timestamp with time zone,
    "ValidUntil" timestamp with time zone,
    "Reason" character varying(1000),
    "CreatedBy" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ModulusScopedGrants" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ModulusSodRules" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "Name" character varying(200) NOT NULL,
    "NormalizedName" character varying(200) NOT NULL,
    "Permissions" character varying(4000) NOT NULL,
    "Rationale" character varying(2000),
    "IsEnabled" boolean NOT NULL,
    "CreatedBy" uuid,
    "CreatedAt" timestamp with time zone NOT NULL,
    "UpdatedAt" timestamp with time zone,
    CONSTRAINT "PK_ModulusSodRules" PRIMARY KEY ("Id")
);

CREATE TABLE IF NOT EXISTS "ModulusTenantPlans" (
    "TenantId" uuid NOT NULL,
    "Plan" character varying(128) NOT NULL,
    CONSTRAINT "PK_ModulusTenantPlans" PRIMARY KEY ("TenantId")
);

CREATE TABLE IF NOT EXISTS "ModulusRecertificationItems" (
    "Id" uuid NOT NULL,
    "TenantId" uuid NOT NULL,
    "CampaignId" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Permission" text NOT NULL,
    "Source" integer NOT NULL,
    "Decision" integer NOT NULL,
    "ReviewedAt" timestamp with time zone,
    "ReviewedBy" uuid,
    CONSTRAINT "PK_ModulusRecertificationItems" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_ModulusRecertificationItems_ModulusRecertificationCampaigns~" FOREIGN KEY ("CampaignId") REFERENCES "ModulusRecertificationCampaigns" ("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_ModulusAccessRequests_TenantId_Status_RequesterId" ON "ModulusAccessRequests" ("TenantId", "Status", "RequesterId");

CREATE INDEX IF NOT EXISTS "IX_ModulusApprovalAuthorities_TenantId_Permission_HolderType_H~" ON "ModulusApprovalAuthorities" ("TenantId", "Permission", "HolderType", "Holder");

CREATE INDEX IF NOT EXISTS "IX_ModulusApprovalChains_TenantId_DocumentType" ON "ModulusApprovalChains" ("TenantId", "DocumentType");

CREATE INDEX IF NOT EXISTS "IX_ModulusApprovalRequests_TenantId_DocumentType_DocumentId" ON "ModulusApprovalRequests" ("TenantId", "DocumentType", "DocumentId");

CREATE INDEX IF NOT EXISTS "IX_ModulusApprovalRequests_TenantId_RequestedBy" ON "ModulusApprovalRequests" ("TenantId", "RequestedBy");

CREATE INDEX IF NOT EXISTS "IX_ModulusApprovalRequests_TenantId_Status_CreatedAt" ON "ModulusApprovalRequests" ("TenantId", "Status", "CreatedAt");

CREATE INDEX IF NOT EXISTS "IX_ModulusAuthorizationAuditOutbox_ProcessedAt_NextAttemptAt" ON "ModulusAuthorizationAuditOutbox" ("ProcessedAt", "NextAttemptAt");

CREATE INDEX IF NOT EXISTS "IX_ModulusDataQualityFindings_CreatedAt" ON "ModulusDataQualityFindings" ("CreatedAt");

CREATE INDEX IF NOT EXISTS "IX_ModulusDataQualityFindings_TenantId_CreatedAt" ON "ModulusDataQualityFindings" ("TenantId", "CreatedAt");

CREATE INDEX IF NOT EXISTS "IX_ModulusDelegations_TenantId_ToUserId" ON "ModulusDelegations" ("TenantId", "ToUserId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_ModulusOrgUnitProfiles_TenantId_NormalizedCode" ON "ModulusOrgUnitProfiles" ("TenantId", "NormalizedCode");

CREATE INDEX IF NOT EXISTS "IX_ModulusOrgUnits_TenantId" ON "ModulusOrgUnits" ("TenantId");

CREATE INDEX IF NOT EXISTS "IX_ModulusPartyLinks_TenantId_Kind_PartyId" ON "ModulusPartyLinks" ("TenantId", "Kind", "PartyId");

CREATE INDEX IF NOT EXISTS "IX_ModulusPositionAssignments_TenantId_PositionId" ON "ModulusPositionAssignments" ("TenantId", "PositionId");

CREATE INDEX IF NOT EXISTS "IX_ModulusPositionAssignments_TenantId_UserId" ON "ModulusPositionAssignments" ("TenantId", "UserId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_ModulusPositions_TenantId_NormalizedCode" ON "ModulusPositions" ("TenantId", "NormalizedCode");

CREATE INDEX IF NOT EXISTS "IX_ModulusRecertificationCampaigns_TenantId_CompletedAt" ON "ModulusRecertificationCampaigns" ("TenantId", "CompletedAt");

CREATE INDEX IF NOT EXISTS "IX_ModulusRecertificationItems_CampaignId_Decision" ON "ModulusRecertificationItems" ("CampaignId", "Decision");

CREATE INDEX IF NOT EXISTS "IX_ModulusRecertificationItems_CampaignId_UserId" ON "ModulusRecertificationItems" ("CampaignId", "UserId");

CREATE INDEX IF NOT EXISTS "IX_ModulusRecertificationItems_TenantId_CampaignId" ON "ModulusRecertificationItems" ("TenantId", "CampaignId");

CREATE INDEX IF NOT EXISTS "IX_ModulusRecordShares_TenantId_ResourceType_ResourceId" ON "ModulusRecordShares" ("TenantId", "ResourceType", "ResourceId");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_ModulusScopedGrants_TenantId_HolderType_Holder_Permission_T~" ON "ModulusScopedGrants" ("TenantId", "HolderType", "Holder", "Permission", "Type", "Scope");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_ModulusSodRules_TenantId_NormalizedName" ON "ModulusSodRules" ("TenantId", "NormalizedName");

COMMIT;
