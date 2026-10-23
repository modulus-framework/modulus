/*
  Upgrade for existing SQL Server deployments, AUTHORIZATION database: the identity platform changes of 2026-10 (VER-2).

  Run as the schema owner, never as an application login, once, before the new release starts:
    sqlcmd -S "$SERVER" -d "$DATABASE" -U "$OWNER" -P "$OWNER_PASSWORD" -b -v api_owner=MyApp.Api -i identity-platform-2026-10.authorization.sqlserver.sql

  api_owner is the owner of the permission catalog rows recorded so far: the API host's assembly name (PermissionCatalogOptions.Owner).
  Idempotent: a second run changes nothing. Existing deployments only: it expects the catalog and grant tables to exist.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

-- 1. Permission catalog: one set of rows per host.
IF COL_LENGTH('ModulusPermissionCatalog', 'Owner') IS NULL
    ALTER TABLE ModulusPermissionCatalog ADD Owner nvarchar(128) NULL;
GO

UPDATE ModulusPermissionCatalog SET Owner = N'$(api_owner)' WHERE Owner IS NULL;
ALTER TABLE ModulusPermissionCatalog ALTER COLUMN Owner nvarchar(128) NOT NULL;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.key_constraints k
    JOIN sys.index_columns ic ON ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id
    JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
    WHERE k.parent_object_id = OBJECT_ID('ModulusPermissionCatalog') AND k.type = 'PK' AND c.name = 'Owner')
BEGIN
    DECLARE @pk sysname = (SELECT name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('ModulusPermissionCatalog') AND type = 'PK');
    EXEC ('ALTER TABLE ModulusPermissionCatalog DROP CONSTRAINT [' + @pk + ']');
    ALTER TABLE ModulusPermissionCatalog ADD CONSTRAINT PK_ModulusPermissionCatalog PRIMARY KEY (Owner, Permission);
END
GO

-- 2. First-boot grant seed marker. A database that already holds grants is marked as seeded, so an upgrade never re-applies the seed
--    over grants an administrator has changed. An empty database is seeded on its first boot.
IF OBJECT_ID('ModulusAuthorizationSeeds') IS NULL
    CREATE TABLE ModulusAuthorizationSeeds (
        Name nvarchar(128) NOT NULL CONSTRAINT PK_ModulusAuthorizationSeeds PRIMARY KEY,
        AppliedAtUtc datetimeoffset NOT NULL);
GO

IF OBJECT_ID('ModulusPermissionGrants') IS NOT NULL
   AND EXISTS (SELECT 1 FROM ModulusPermissionGrants)
   AND NOT EXISTS (SELECT 1 FROM ModulusAuthorizationSeeds WHERE Name = N'permission-grants')
    INSERT INTO ModulusAuthorizationSeeds (Name, AppliedAtUtc) VALUES (N'permission-grants', SYSDATETIMEOFFSET());
GO

-- 3. Dimension values (scope codes per company). A code is unique within its dimension and company.
IF OBJECT_ID('ModulusDimensionValues') IS NULL
    CREATE TABLE ModulusDimensionValues (
        TenantId uniqueidentifier NOT NULL,
        Id uniqueidentifier NOT NULL,
        Dimension nvarchar(64) NOT NULL,
        Code nvarchar(64) NOT NULL,
        Label nvarchar(128) NOT NULL,
        ParentId uniqueidentifier NULL,
        CONSTRAINT PK_ModulusDimensionValues PRIMARY KEY (TenantId, Id));
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ModulusDimensionValues_TenantId_Dimension_Code' AND object_id = OBJECT_ID('ModulusDimensionValues'))
    CREATE UNIQUE INDEX IX_ModulusDimensionValues_TenantId_Dimension_Code ON ModulusDimensionValues (TenantId, Dimension, Code);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ModulusDimensionValues_TenantId_ParentId' AND object_id = OBJECT_ID('ModulusDimensionValues'))
    CREATE INDEX IX_ModulusDimensionValues_TenantId_ParentId ON ModulusDimensionValues (TenantId, ParentId);
GO

COMMIT TRANSACTION;
GO
