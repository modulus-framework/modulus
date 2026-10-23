/*
  Upgrade for existing SQL Server deployments, TENANCY database: the identity platform changes of 2026-10.

  Run as the schema owner, never as an application login, once, before the new release starts:
    sqlcmd -S "$SERVER" -d "$DATABASE" -U "$OWNER" -P "$OWNER_PASSWORD" -b -i identity-platform-2026-10.tenancy.sqlserver.sql

  Adds the company lifecycle columns (Status, TrialEndsAt, MaxUsers) to ModulusTenants and creates ModulusTenantDataStores and ModulusSettingValues.
  Idempotent: a second run changes nothing. Existing companies start as Active with no trial end and no seat limit.
  Existing deployments only: it expects ModulusTenants to exist.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

-- 1. Company lifecycle. Status is the TenantStatus enum stored as its integer value (Trial 0, Active 1, Suspended 2, Closed 3).
IF COL_LENGTH('ModulusTenants', 'Status') IS NULL
    ALTER TABLE ModulusTenants ADD Status int NOT NULL CONSTRAINT DF_ModulusTenants_Status DEFAULT 1;
GO

IF COL_LENGTH('ModulusTenants', 'TrialEndsAt') IS NULL
    ALTER TABLE ModulusTenants ADD TrialEndsAt datetimeoffset(7) NULL;
GO

IF COL_LENGTH('ModulusTenants', 'MaxUsers') IS NULL
    ALTER TABLE ModulusTenants ADD MaxUsers int NULL;
GO

-- 2. Tenant data stores: a pointer to a company's database secret, never a connection string.
IF OBJECT_ID('ModulusTenantDataStores', 'U') IS NULL
    CREATE TABLE ModulusTenantDataStores (
        TenantId uniqueidentifier NOT NULL,
        StoreKey nvarchar(32) NOT NULL,
        Isolation nvarchar(32) NOT NULL,
        ProviderKey nvarchar(32) NOT NULL,
        SecretPath nvarchar(256) NOT NULL,
        SecretVersion int NOT NULL CONSTRAINT DF_ModulusTenantDataStores_SecretVersion DEFAULT 0,
        Status nvarchar(32) NOT NULL,
        CONSTRAINT PK_ModulusTenantDataStores PRIMARY KEY (TenantId, StoreKey)
    );
GO

-- 3. Setting values: one row per setting and scope (installation, company or person). ScopeKey is a non-null text column, so
--    (Name, ScopeKey) is unique at every scope, the installation's included, on every engine.
IF OBJECT_ID('ModulusSettingValues', 'U') IS NULL
    CREATE TABLE ModulusSettingValues (
        Name nvarchar(128) NOT NULL,
        ScopeKey nvarchar(80) NOT NULL,
        TenantId uniqueidentifier NULL,
        UserId uniqueidentifier NULL,
        Value nvarchar(4000) NOT NULL,
        CONSTRAINT PK_ModulusSettingValues PRIMARY KEY (Name, ScopeKey)
    );

-- 4. Settings change counter: every settings write advances it, and the other nodes check it on a schedule, so a change reaches them
--    without Redis. The first write creates the row.
IF OBJECT_ID('ModulusSettingVersions') IS NULL
    CREATE TABLE ModulusSettingVersions (
        Name nvarchar(64) NOT NULL CONSTRAINT PK_ModulusSettingVersions PRIMARY KEY,
        Version bigint NOT NULL);
GO

COMMIT;
GO
