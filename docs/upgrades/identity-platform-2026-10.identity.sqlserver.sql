/*
  Upgrade for existing SQL Server deployments, IDENTITY database: the account lifecycle, principal kind, ERP identifiers and the lifecycle
  history of 2026-10 (VER-2).

  Run as the schema owner, once, before the new release starts. The user table is the application's identity user table (ASP.NET
  Identity's default is AspNetUsers):
    sqlcmd -S "$SERVER" -d "$DATABASE" -U "$OWNER" -P "$OWNER_PASSWORD" -b -v user_table=AspNetUsers -i identity-platform-2026-10.identity.sqlserver.sql

  Accounts disabled through IsActive = 0 are carried over to the Disabled lifecycle state, so they stay disabled. Only accounts whose
  lifecycle has never changed are carried over, so a rerun cannot disable an account the new release has since re-enabled.
  The ERP identifier columns (EmployeeCode, ExternalId) are nvarchar(450), the length EF Core gives an indexed string on SQL Server.
  Idempotent: a second run changes nothing. Existing deployments only.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

IF COL_LENGTH('$(user_table)', 'PrincipalKind') IS NULL
    ALTER TABLE [$(user_table)] ADD PrincipalKind nvarchar(32) NOT NULL CONSTRAINT DF_$(user_table)_PrincipalKind DEFAULT N'person';
IF COL_LENGTH('$(user_table)', 'Lifecycle') IS NULL
    ALTER TABLE [$(user_table)] ADD Lifecycle nvarchar(32) NOT NULL CONSTRAINT DF_$(user_table)_Lifecycle DEFAULT N'Active';
IF COL_LENGTH('$(user_table)', 'LifecycleChangedAt') IS NULL
    ALTER TABLE [$(user_table)] ADD LifecycleChangedAt datetimeoffset NULL;
IF COL_LENGTH('$(user_table)', 'SuspensionReason') IS NULL
    ALTER TABLE [$(user_table)] ADD SuspensionReason nvarchar(64) NULL;
IF COL_LENGTH('$(user_table)', 'SuspensionNote') IS NULL
    ALTER TABLE [$(user_table)] ADD SuspensionNote nvarchar(512) NULL;
IF COL_LENGTH('$(user_table)', 'MustChangePin') IS NULL
    ALTER TABLE [$(user_table)] ADD MustChangePin bit NOT NULL CONSTRAINT DF_$(user_table)_MustChangePin DEFAULT 0;
IF COL_LENGTH('$(user_table)', 'EmployeeCode') IS NULL
    ALTER TABLE [$(user_table)] ADD EmployeeCode nvarchar(450) NULL;
IF COL_LENGTH('$(user_table)', 'PinHash') IS NULL
    ALTER TABLE [$(user_table)] ADD PinHash nvarchar(max) NULL;
IF COL_LENGTH('$(user_table)', 'ExternalId') IS NULL
    ALTER TABLE [$(user_table)] ADD ExternalId nvarchar(450) NULL;
IF COL_LENGTH('$(user_table)', 'ExtraProperties') IS NULL
    ALTER TABLE [$(user_table)] ADD ExtraProperties nvarchar(max) NOT NULL CONSTRAINT DF_$(user_table)_ExtraProperties DEFAULT N'{}';
GO

UPDATE [$(user_table)] SET Lifecycle = N'Disabled'
WHERE IsActive = 0 AND Lifecycle = N'Active' AND LifecycleChangedAt IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AspNetUsers_TenantId_EmployeeCode' AND object_id = OBJECT_ID('[$(user_table)]'))
    CREATE INDEX IX_AspNetUsers_TenantId_EmployeeCode ON [$(user_table)] (TenantId, EmployeeCode);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AspNetUsers_TenantId_ExternalId' AND object_id = OBJECT_ID('[$(user_table)]'))
    CREATE INDEX IX_AspNetUsers_TenantId_ExternalId ON [$(user_table)] (TenantId, ExternalId);
GO

IF OBJECT_ID('ModulusAccountTransitions') IS NULL
    CREATE TABLE ModulusAccountTransitions (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ModulusAccountTransitions PRIMARY KEY,
        UserId uniqueidentifier NOT NULL,
        FromState nvarchar(32) NOT NULL,
        ToState nvarchar(32) NOT NULL,
        ActorId uniqueidentifier NULL,
        Reason nvarchar(512) NULL,
        OccurredAt datetimeoffset NOT NULL);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ModulusAccountTransitions_UserId_Id' AND object_id = OBJECT_ID('ModulusAccountTransitions'))
    CREATE INDEX IX_ModulusAccountTransitions_UserId_Id ON ModulusAccountTransitions (UserId, Id);
GO

COMMIT TRANSACTION;
GO
