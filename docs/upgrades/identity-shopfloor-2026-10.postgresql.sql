-- Upgrade for existing PostgreSQL deployments, IDENTITY database: the shop-floor sign-in columns (employee code, PIN hash, forced PIN
-- change) and the employee-code index. Needed only when the application enables shop-floor sign-in (Identity:ShopFloor:Enabled).
--
-- Run as the schema owner, once, before the new release starts:
--   psql "$CONNECTION" -v ON_ERROR_STOP=1 -v user_table=AspNetUsers -f identity-shopfloor-2026-10.postgresql.sql
--
-- The columns are nullable, except the forced-change flag, which starts false. PostgreSQL treats NULLs as distinct in a unique index,
-- so the employee-code index is filtered to accounts that have a code.
--
-- Refusal, not repair: if two accounts in one company already share an employee code, the script stops before it changes anything and
-- prints the duplicates. Resolve them, then rerun. The script never deletes or rewrites employee data.
--
-- Idempotent. Existing deployments only.

BEGIN;

-- 1. Preflight: nothing is changed when duplicate codes exist. On a database without the column yet, there is nothing to check.
SELECT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = :'user_table' AND column_name = 'EmployeeCode') AS has_employee_code \gset
\if :has_employee_code
SELECT EXISTS (
    SELECT 1 FROM :"user_table" WHERE "EmployeeCode" IS NOT NULL
    GROUP BY "TenantId", "EmployeeCode" HAVING count(*) > 1) AS duplicates_present \gset
\if :duplicates_present
\echo 'REFUSED: duplicate employee codes exist within a company. Nothing was changed. Find them with:'
\echo '  SELECT "TenantId", "EmployeeCode", count(*) FROM "AspNetUsers" WHERE "EmployeeCode" IS NOT NULL GROUP BY 1, 2 HAVING count(*) > 1;'
SELECT 1 / 0 AS refused_duplicate_employee_codes;
\endif
\endif

ALTER TABLE :"user_table" ADD COLUMN IF NOT EXISTS "EmployeeCode" varchar(32);
ALTER TABLE :"user_table" ADD COLUMN IF NOT EXISTS "PinHash" text;
ALTER TABLE :"user_table" ADD COLUMN IF NOT EXISTS "MustChangePin" boolean NOT NULL DEFAULT false;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_AspNetUsers_TenantId_EmployeeCode_Unique" ON :"user_table" ("TenantId", "EmployeeCode") WHERE "EmployeeCode" IS NOT NULL;

COMMIT;
