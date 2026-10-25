-- Upgrade for existing SQL Server deployments, IDENTITY database: the profile details of premodule 3 (work e-mail, job title, department,
-- primary branch, profile photo, pending work e-mail confirmation) and a company-scoped employee code.
--
-- Run as the schema owner, once, before the new release starts:
--   sqlcmd -S "$SERVER" -d "$DATABASE" -U "$OWNER" -P "$OWNER_PASSWORD" -b -v user_table=AspNetUsers -i identity-platform-2026-10.profile.sqlserver.sql
--
-- Every new column is nullable, except the confirmation failure counter, which starts at 0. SQL Server treats NULLs as equal in a unique
-- index, so the employee-code index is filtered to accounts that have a code.
--
-- Refusal, not repair: if two accounts in one company already share an employee code, the script stops before it changes anything and
-- reports it (-b makes sqlcmd exit with an error). Resolve the duplicates, then rerun. The script never deletes or rewrites employee data.
--
-- Idempotent. Existing deployments only.

-- The filtered index needs these session options; set them here so the script does not depend on the client's defaults.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;

-- 1. Preflight: nothing is changed when duplicate codes exist.
IF EXISTS (SELECT 1 FROM [$(user_table)] WHERE EmployeeCode IS NOT NULL GROUP BY TenantId, EmployeeCode HAVING COUNT(*) > 1)
BEGIN
    RAISERROR('REFUSED: duplicate employee codes exist within a company. Nothing was changed. Find them with SELECT TenantId, EmployeeCode, COUNT(*) FROM [AspNetUsers] WHERE EmployeeCode IS NOT NULL GROUP BY TenantId, EmployeeCode HAVING COUNT(*) > 1; resolve them, then rerun.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;

IF COL_LENGTH('$(user_table)', 'WorkEmail') IS NULL
    ALTER TABLE [$(user_table)] ADD WorkEmail nvarchar(256) NULL;
IF COL_LENGTH('$(user_table)', 'JobTitle') IS NULL
    ALTER TABLE [$(user_table)] ADD JobTitle nvarchar(100) NULL;
IF COL_LENGTH('$(user_table)', 'Department') IS NULL
    ALTER TABLE [$(user_table)] ADD Department nvarchar(100) NULL;
IF COL_LENGTH('$(user_table)', 'PrimaryBranchId') IS NULL
    ALTER TABLE [$(user_table)] ADD PrimaryBranchId uniqueidentifier NULL;
IF COL_LENGTH('$(user_table)', 'PhotoAssetId') IS NULL
    ALTER TABLE [$(user_table)] ADD PhotoAssetId uniqueidentifier NULL;
IF COL_LENGTH('$(user_table)', 'PendingWorkEmail') IS NULL
    ALTER TABLE [$(user_table)] ADD PendingWorkEmail nvarchar(256) NULL;
IF COL_LENGTH('$(user_table)', 'WorkEmailVerificationStamp') IS NULL
    ALTER TABLE [$(user_table)] ADD WorkEmailVerificationStamp nvarchar(64) NULL;
IF COL_LENGTH('$(user_table)', 'WorkEmailRequestedAt') IS NULL
    ALTER TABLE [$(user_table)] ADD WorkEmailRequestedAt datetimeoffset NULL;
IF COL_LENGTH('$(user_table)', 'ConfirmationFailures') IS NULL
    ALTER TABLE [$(user_table)] ADD ConfirmationFailures int NOT NULL CONSTRAINT DF_$(user_table)_ConfirmationFailures DEFAULT 0;

-- The unique index comes first, so a failure here rolls the transaction back and the old index is still in place.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AspNetUsers_TenantId_EmployeeCode_Unique' AND object_id = OBJECT_ID('[$(user_table)]'))
    CREATE UNIQUE INDEX IX_AspNetUsers_TenantId_EmployeeCode_Unique ON [$(user_table)] (TenantId, EmployeeCode) WHERE EmployeeCode IS NOT NULL;

-- The earlier non-unique index on the same columns is redundant now, so it goes: the index set matches a fresh install.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AspNetUsers_TenantId_EmployeeCode' AND object_id = OBJECT_ID('[$(user_table)]'))
    DROP INDEX IX_AspNetUsers_TenantId_EmployeeCode ON [$(user_table)];

COMMIT TRANSACTION;
