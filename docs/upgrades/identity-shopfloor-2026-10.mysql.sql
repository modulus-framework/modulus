-- Upgrade for existing MySQL deployments, IDENTITY database: the shop-floor sign-in columns (employee code, PIN hash, forced PIN change)
-- and the employee-code index. Needed only when the application enables shop-floor sign-in (Identity:ShopFloor:Enabled).
--
-- Run as the schema owner, once, before the new release starts. Replace AspNetUsers below if the table has another name.
--
-- MySQL cannot add a column only when it is missing, and its DDL is not transactional. So the steps run in order, and each one is safe
-- to stop after:
--   1. The three columns are added. They are nullable (the forced-change flag starts false), so existing rows are unaffected.
--   2. The duplicate check lists companies that already share an employee code. Nothing is changed by it. If it returns rows, stop
--      and resolve them first.
--   3. The unique index is created. If duplicates exist, this step fails with "Duplicate entry" and no index is created.
-- MySQL allows any number of NULLs in a unique index, so the index needs no filter.
--
-- Rerunning: step 1 fails with "Duplicate column name" once the columns exist. Delete the ALTER statements that already succeeded, and
-- run the rest again. The script never deletes or rewrites employee data.
--
-- Existing deployments only.

-- 1. Columns.
ALTER TABLE AspNetUsers ADD COLUMN EmployeeCode varchar(32) NULL;
ALTER TABLE AspNetUsers ADD COLUMN PinHash varchar(512) NULL;
ALTER TABLE AspNetUsers ADD COLUMN MustChangePin tinyint(1) NOT NULL DEFAULT 0;

-- 2. Preflight: this must return no rows before step 3 is run.
SELECT TenantId, EmployeeCode, COUNT(*) AS duplicates
FROM AspNetUsers
WHERE EmployeeCode IS NOT NULL
GROUP BY TenantId, EmployeeCode
HAVING COUNT(*) > 1;

-- 3. The unique index on the employee code within a company.
CREATE UNIQUE INDEX IX_AspNetUsers_TenantId_EmployeeCode_Unique ON AspNetUsers (TenantId, EmployeeCode);
