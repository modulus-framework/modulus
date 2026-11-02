-- Upgrade for existing SQL Server deployments, IDENTITY database: the shop-floor sign-in columns (employee code, PIN hash, forced PIN change)
-- and the employee-code index. Needed only when the application enables shop-floor sign-in (Identity:ShopFloor:Enabled).
--
-- Run as the schema owner, once, before the new release starts:
--   sqlcmd -S "$SERVER" -d "$DATABASE" -U "$OWNER" -P "$OWNER_PASSWORD" -b -v user_table=AspNetUsers -i identity-shopfloor-2026-10.sqlserver.sql
--
-- The columns are nullable, except the forced-change flag, which starts false. SQL Server treats NULLs as equal in a unique index, so the
-- employee-code index is filtered to accounts that have a code.
--
-- Refusal, not repair: if two accounts in one company already share an employee code, the script stops before it changes anything and
-- reports it (-b makes sqlcmd exit with an error). Resolve the duplicates, then rerun. The script never deletes or rewrites employee data.
--
-- Idempotent. Existing deployments only.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;

-- 1. Preflight: nothing is changed when duplicate codes exist. The check is dynamic SQL, so it is compiled only once the column exists
--    (a first install has no column yet, and SQL Server would reject the batch when it compiles the reference).
IF COL_LENGTH('$(user_table)', 'EmployeeCode') IS NOT NULL
BEGIN
    DECLARE @duplicates int;
    EXEC sp_executesql
        N'SELECT @d = COUNT(*) FROM (SELECT TenantId, EmployeeCode FROM [$(user_table)] WHERE EmployeeCode IS NOT NULL GROUP BY TenantId, EmployeeCode HAVING COUNT(*) > 1) x;',
        N'@d int OUTPUT',
        @d = @duplicates OUTPUT;
    IF @duplicates > 0
    BEGIN
        RAISERROR('REFUSED: duplicate employee codes exist within a company. Nothing was changed. Find them with SELECT TenantId, EmployeeCode, COUNT(*) FROM [AspNetUsers] WHERE EmployeeCode IS NOT NULL GROUP BY TenantId, EmployeeCode HAVING COUNT(*) > 1;', 16, 1);
        RETURN;
    END
END

BEGIN TRANSACTION;

IF COL_LENGTH('$(user_table)', 'EmployeeCode') IS NULL
    ALTER TABLE [$(user_table)] ADD EmployeeCode nvarchar(32) NULL;
IF COL_LENGTH('$(user_table)', 'PinHash') IS NULL
    ALTER TABLE [$(user_table)] ADD PinHash nvarchar(max) NULL;
IF COL_LENGTH('$(user_table)', 'MustChangePin') IS NULL
    ALTER TABLE [$(user_table)] ADD MustChangePin bit NOT NULL CONSTRAINT DF_$(user_table)_MustChangePin DEFAULT 0;

-- A batch separator: the index refers to the column added above, which SQL Server must see as existing when it compiles the index.
GO

-- The unique index comes first, so a failure here rolls the transaction back.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AspNetUsers_TenantId_EmployeeCode_Unique' AND object_id = OBJECT_ID('[$(user_table)]'))
    CREATE UNIQUE INDEX IX_AspNetUsers_TenantId_EmployeeCode_Unique ON [$(user_table)] (TenantId, EmployeeCode) WHERE EmployeeCode IS NOT NULL;

COMMIT TRANSACTION;
