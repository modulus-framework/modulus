-- Upgrade for existing PostgreSQL deployments, IDENTITY database: the profile details of premodule 3 (work e-mail, job title, department,
-- primary branch, profile photo, pending work e-mail confirmation) and a company-scoped employee code.
--
-- Run as the schema owner, once, before the new release starts:
--   psql "$IDENTITY_CONNECTION" -v ON_ERROR_STOP=1 -v user_table=AspNetUsers -f identity-platform-2026-10.profile.postgresql.sql
--
-- Every new column is nullable, except the confirmation failure counter, which starts at 0. Existing accounts keep their values.
-- The employee code is unique within a company (TenantId) when it is set; accounts without one are outside the index.
--
-- Refusal, not repair: if two accounts in one company already share an employee code, the script stops before it changes anything and
-- prints the duplicates. Resolve them in the application (or by hand), then rerun. The script never deletes or rewrites employee data.
--
-- Idempotent: a second run changes nothing. Existing deployments only.

-- 1. Preflight: stop before any change when duplicate codes exist. The \if branch exits with an error (ON_ERROR_STOP is set).
SELECT EXISTS (
    SELECT 1 FROM :"user_table" WHERE "EmployeeCode" IS NOT NULL
    GROUP BY "TenantId", "EmployeeCode" HAVING count(*) > 1) AS duplicates_present \gset
\if :duplicates_present
\echo 'REFUSED: duplicate employee codes exist within a company. Nothing was changed. Find them with:'
\echo '  SELECT "TenantId", "EmployeeCode", count(*) FROM "AspNetUsers" WHERE "EmployeeCode" IS NOT NULL GROUP BY 1, 2 HAVING count(*) > 1;'
\echo 'Resolve them, then rerun this script.'
SELECT 1 / 0 AS refused_duplicate_employee_codes;
\endif

BEGIN;

ALTER TABLE :"user_table" ADD COLUMN IF NOT EXISTS "WorkEmail" varchar(256) NULL;
ALTER TABLE :"user_table" ADD COLUMN IF NOT EXISTS "JobTitle" varchar(100) NULL;
ALTER TABLE :"user_table" ADD COLUMN IF NOT EXISTS "Department" varchar(100) NULL;
ALTER TABLE :"user_table" ADD COLUMN IF NOT EXISTS "PrimaryBranchId" uuid NULL;
ALTER TABLE :"user_table" ADD COLUMN IF NOT EXISTS "PhotoAssetId" uuid NULL;
ALTER TABLE :"user_table" ADD COLUMN IF NOT EXISTS "PendingWorkEmail" varchar(256) NULL;
ALTER TABLE :"user_table" ADD COLUMN IF NOT EXISTS "WorkEmailVerificationStamp" varchar(64) NULL;
ALTER TABLE :"user_table" ADD COLUMN IF NOT EXISTS "WorkEmailRequestedAt" timestamptz NULL;
ALTER TABLE :"user_table" ADD COLUMN IF NOT EXISTS "ConfirmationFailures" integer NOT NULL DEFAULT 0;

-- The unique index comes first, so a failure here rolls the whole transaction back and the old index is still in place.
CREATE UNIQUE INDEX IF NOT EXISTS "IX_AspNetUsers_TenantId_EmployeeCode_Unique" ON :"user_table" ("TenantId", "EmployeeCode") WHERE "EmployeeCode" IS NOT NULL;
-- The earlier non-unique index on the same columns is redundant now, so it goes: the index set matches a fresh install.
DROP INDEX IF EXISTS "IX_AspNetUsers_TenantId_EmployeeCode";

COMMIT;
