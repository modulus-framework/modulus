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

-- 4. Permission exceptions: the register of privileged-permission exceptions (who keeps a Critical permission, why, until when).
IF OBJECT_ID('ModulusPermissionExceptions') IS NULL
    CREATE TABLE ModulusPermissionExceptions (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ModulusPermissionExceptions PRIMARY KEY,
        TenantId uniqueidentifier NOT NULL,
        Permission nvarchar(200) NOT NULL,
        HolderType int NOT NULL,
        Holder nvarchar(200) NOT NULL,
        Justification nvarchar(2000) NOT NULL,
        CompensatingControl nvarchar(2000) NULL,
        BusinessOwner nvarchar(200) NOT NULL,
        RequestedBy uniqueidentifier NOT NULL,
        ValidFrom datetimeoffset NOT NULL,
        ValidUntil datetimeoffset NOT NULL,
        ReviewDueOn datetimeoffset NOT NULL,
        CreatedAt datetimeoffset NOT NULL,
        ReviewedBy uniqueidentifier NULL,
        ReviewedAt datetimeoffset NULL,
        RevokedBy uniqueidentifier NULL,
        RevokedAt datetimeoffset NULL);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PermissionExceptions_Holder' AND object_id = OBJECT_ID('ModulusPermissionExceptions'))
    CREATE INDEX IX_PermissionExceptions_Holder ON ModulusPermissionExceptions (TenantId, Permission, HolderType, Holder);
GO

-- 5. The remaining authorization tables: every table of the authorization store that the steps above do not create (generated from the
--    EF model, 2026-10-23). Each statement is idempotent: an existing table or index is left as it is.
IF OBJECT_ID('ModulusAccessRequests') IS NULL
CREATE TABLE [ModulusAccessRequests] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [Kind] int NOT NULL,
    [RequesterId] uniqueidentifier NOT NULL,
    [Permissions] nvarchar(4000) NOT NULL,
    [Reason] nvarchar(2000) NOT NULL,
    [Hours] int NOT NULL,
    [Status] int NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [DecidedBy] uniqueidentifier NULL,
    [DecidedAt] datetimeoffset NULL,
    [Note] nvarchar(2000) NULL,
    [AccessEndsAt] datetimeoffset NULL,
    [ReviewedBy] uniqueidentifier NULL,
    [ReviewedAt] datetimeoffset NULL,
    CONSTRAINT [PK_ModulusAccessRequests] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID('ModulusApprovalAuthorities') IS NULL
CREATE TABLE [ModulusApprovalAuthorities] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [HolderType] int NOT NULL,
    [Holder] nvarchar(256) NOT NULL,
    [Permission] nvarchar(256) NOT NULL,
    [MaxAmount] decimal(18,4) NOT NULL,
    [Currency] nvarchar(8) NULL,
    [DocumentType] nvarchar(256) NULL,
    [OrgUnitId] uniqueidentifier NULL,
    [ValidFrom] datetimeoffset NULL,
    [ValidUntil] datetimeoffset NULL,
    [CreatedBy] uniqueidentifier NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_ModulusApprovalAuthorities] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID('ModulusApprovalChains') IS NULL
CREATE TABLE [ModulusApprovalChains] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [Name] nvarchar(256) NOT NULL,
    [DocumentType] nvarchar(256) NOT NULL,
    [MinAmount] decimal(18,4) NULL,
    [MaxAmount] decimal(18,4) NULL,
    [Currency] nvarchar(8) NULL,
    [Steps] nvarchar(max) NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_ModulusApprovalChains] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID('ModulusApprovalRequests') IS NULL
CREATE TABLE [ModulusApprovalRequests] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [ChainId] uniqueidentifier NOT NULL,
    [DocumentType] nvarchar(256) NOT NULL,
    [DocumentId] nvarchar(256) NOT NULL,
    [Amount] decimal(18,4) NOT NULL,
    [Currency] nvarchar(8) NULL,
    [OrgUnitId] uniqueidentifier NULL,
    [RequestedBy] uniqueidentifier NOT NULL,
    [Summary] nvarchar(1000) NULL,
    [Steps] nvarchar(max) NOT NULL,
    [CurrentStep] int NOT NULL,
    [Status] int NOT NULL,
    [Decisions] nvarchar(max) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [StepStartedAt] datetimeoffset NOT NULL,
    [Escalated] bit NOT NULL,
    [Version] bigint NOT NULL,
    [CompletedAt] datetimeoffset NULL,
    CONSTRAINT [PK_ModulusApprovalRequests] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID('ModulusAssignments') IS NULL
CREATE TABLE [ModulusAssignments] (
    [TenantId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [AssignmentType] nvarchar(128) NOT NULL,
    [TargetId] uniqueidentifier NOT NULL,
    [ValidFrom] datetimeoffset NULL,
    [ValidUntil] datetimeoffset NULL,
    CONSTRAINT [PK_ModulusAssignments] PRIMARY KEY ([TenantId], [UserId], [AssignmentType], [TargetId])
);
GO

IF OBJECT_ID('ModulusAuthorizationAuditOutbox') IS NULL
CREATE TABLE [ModulusAuthorizationAuditOutbox] (
    [Id] uniqueidentifier NOT NULL,
    [MessageType] nvarchar(256) NOT NULL,
    [Payload] nvarchar(max) NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [ModuleName] nvarchar(max) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ProcessedAt] datetime2 NULL,
    [LockedBy] nvarchar(max) NULL,
    [LockedUntil] datetime2 NULL,
    [NextAttemptAt] datetime2 NULL,
    [RetryCount] int NOT NULL,
    [Error] nvarchar(max) NULL,
    [CorrelationId] nvarchar(max) NULL,
    [CausationId] nvarchar(max) NULL,
    [TraceParent] nvarchar(max) NULL,
    [TraceState] nvarchar(max) NULL,
    [SchemaVersion] int NULL,
    CONSTRAINT [PK_ModulusAuthorizationAuditOutbox] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID('ModulusCompanyProfiles') IS NULL
CREATE TABLE [ModulusCompanyProfiles] (
    [TenantId] uniqueidentifier NOT NULL,
    [LegalName] nvarchar(256) NOT NULL,
    [TradeName] nvarchar(256) NULL,
    [RegistrationNumber] nvarchar(64) NULL,
    [TaxId] nvarchar(64) NULL,
    [Address] nvarchar(1000) NULL,
    [Country] nvarchar(2) NULL,
    [Currency] nvarchar(3) NULL,
    [FiscalYearStartMonth] int NOT NULL,
    [TimeZone] nvarchar(64) NULL,
    [Language] nvarchar(16) NULL,
    [UpdatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_ModulusCompanyProfiles] PRIMARY KEY ([TenantId])
);
GO

IF OBJECT_ID('ModulusDataQualityFindings') IS NULL
CREATE TABLE [ModulusDataQualityFindings] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ResourceType] nvarchar(max) NOT NULL,
    [RecordId] nvarchar(max) NULL,
    [Action] nvarchar(max) NOT NULL,
    [MissingAttributes] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_ModulusDataQualityFindings] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID('ModulusDelegations') IS NULL
CREATE TABLE [ModulusDelegations] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [FromUserId] uniqueidentifier NOT NULL,
    [FromRolesJson] nvarchar(max) NOT NULL,
    [ToUserId] uniqueidentifier NOT NULL,
    [PermissionsJson] nvarchar(max) NOT NULL,
    [NotBefore] datetimeoffset NOT NULL,
    [NotAfter] datetimeoffset NOT NULL,
    [Revoked] bit NOT NULL,
    CONSTRAINT [PK_ModulusDelegations] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID('ModulusFeatureOverrides') IS NULL
CREATE TABLE [ModulusFeatureOverrides] (
    [TenantId] uniqueidentifier NOT NULL,
    [Feature] nvarchar(256) NOT NULL,
    [Enabled] bit NOT NULL,
    CONSTRAINT [PK_ModulusFeatureOverrides] PRIMARY KEY ([TenantId], [Feature])
);
GO

IF OBJECT_ID('ModulusOrgPlacements') IS NULL
CREATE TABLE [ModulusOrgPlacements] (
    [TenantId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [OrgUnitId] uniqueidentifier NOT NULL,
    [Mode] int NOT NULL,
    CONSTRAINT [PK_ModulusOrgPlacements] PRIMARY KEY ([TenantId], [UserId], [OrgUnitId])
);
GO

IF OBJECT_ID('ModulusOrgUnitParents') IS NULL
CREATE TABLE [ModulusOrgUnitParents] (
    [TenantId] uniqueidentifier NOT NULL,
    [ChildId] uniqueidentifier NOT NULL,
    [ParentId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_ModulusOrgUnitParents] PRIMARY KEY ([TenantId], [ChildId], [ParentId])
);
GO

IF OBJECT_ID('ModulusOrgUnitProfiles') IS NULL
CREATE TABLE [ModulusOrgUnitProfiles] (
    [TenantId] uniqueidentifier NOT NULL,
    [UnitId] uniqueidentifier NOT NULL,
    [Code] nvarchar(64) NOT NULL,
    [NormalizedCode] nvarchar(64) NOT NULL,
    [Name] nvarchar(256) NOT NULL,
    [Kind] nvarchar(64) NOT NULL,
    [IsClosed] bit NOT NULL,
    [ManagerUserId] uniqueidentifier NULL,
    [ClosedAt] datetimeoffset NULL,
    [UpdatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_ModulusOrgUnitProfiles] PRIMARY KEY ([TenantId], [UnitId])
);
GO

IF OBJECT_ID('ModulusOrgUnits') IS NULL
CREATE TABLE [ModulusOrgUnits] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_ModulusOrgUnits] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID('ModulusPartyCeilings') IS NULL
CREATE TABLE [ModulusPartyCeilings] (
    [TenantId] uniqueidentifier NOT NULL,
    [Kind] nvarchar(64) NOT NULL,
    [Permission] nvarchar(256) NOT NULL,
    CONSTRAINT [PK_ModulusPartyCeilings] PRIMARY KEY ([TenantId], [Kind], [Permission])
);
GO

IF OBJECT_ID('ModulusPartyLinks') IS NULL
CREATE TABLE [ModulusPartyLinks] (
    [TenantId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [Kind] nvarchar(64) NOT NULL,
    [PartyId] uniqueidentifier NOT NULL,
    [CreatedBy] uniqueidentifier NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_ModulusPartyLinks] PRIMARY KEY ([TenantId], [UserId])
);
GO

IF OBJECT_ID('ModulusPlanFeatures') IS NULL
CREATE TABLE [ModulusPlanFeatures] (
    [Plan] nvarchar(128) NOT NULL,
    [Feature] nvarchar(256) NOT NULL,
    CONSTRAINT [PK_ModulusPlanFeatures] PRIMARY KEY ([Plan], [Feature])
);
GO

IF OBJECT_ID('ModulusPositionAssignments') IS NULL
CREATE TABLE [ModulusPositionAssignments] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [PositionId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [ValidFrom] datetimeoffset NOT NULL,
    [ValidUntil] datetimeoffset NULL,
    [CreatedBy] uniqueidentifier NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_ModulusPositionAssignments] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID('ModulusPositions') IS NULL
CREATE TABLE [ModulusPositions] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [Code] nvarchar(100) NOT NULL,
    [NormalizedCode] nvarchar(100) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [OrgUnitId] uniqueidentifier NULL,
    [Roles] nvarchar(4000) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_ModulusPositions] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID('ModulusRecertificationCampaigns') IS NULL
CREATE TABLE [ModulusRecertificationCampaigns] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [CreatedBy] uniqueidentifier NOT NULL,
    [CompletedAt] datetime2 NULL,
    CONSTRAINT [PK_ModulusRecertificationCampaigns] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID('ModulusRecordShares') IS NULL
CREATE TABLE [ModulusRecordShares] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [ResourceType] nvarchar(256) NOT NULL,
    [ResourceId] nvarchar(256) NOT NULL,
    [GranteeUserId] uniqueidentifier NOT NULL,
    [SharedByUserId] uniqueidentifier NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [ExpiresAt] datetimeoffset NOT NULL,
    [RevokedAt] datetimeoffset NULL,
    CONSTRAINT [PK_ModulusRecordShares] PRIMARY KEY ([Id])
);
GO

-- Explicit parent-to-child authorities (decision 6). Revoked rows are kept; host data, so no tenant column.
IF OBJECT_ID('ModulusCompanyAuthorityGrants') IS NULL
CREATE TABLE [ModulusCompanyAuthorityGrants] (
    [Id] uniqueidentifier NOT NULL,
    [ParentId] uniqueidentifier NOT NULL,
    [ChildId] uniqueidentifier NOT NULL,
    [PrincipalId] uniqueidentifier NOT NULL,
    [Operation] nvarchar(32) NOT NULL,
    [GrantedBy] uniqueidentifier NULL,
    [GrantedAt] datetimeoffset NOT NULL,
    [RevokedAt] datetimeoffset NULL,
    [RevokedBy] uniqueidentifier NULL,
    CONSTRAINT [PK_ModulusCompanyAuthorityGrants] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID('ModulusRoleInclusions') IS NULL
CREATE TABLE [ModulusRoleInclusions] (
    [TenantId] uniqueidentifier NOT NULL,
    [NormalizedRole] nvarchar(256) NOT NULL,
    [NormalizedIncludes] nvarchar(256) NOT NULL,
    [Role] nvarchar(256) NOT NULL,
    [Includes] nvarchar(256) NOT NULL,
    [CreatedBy] uniqueidentifier NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_ModulusRoleInclusions] PRIMARY KEY ([TenantId], [NormalizedRole], [NormalizedIncludes])
);
GO

IF OBJECT_ID('ModulusScopedGrants') IS NULL
CREATE TABLE [ModulusScopedGrants] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [HolderType] int NOT NULL,
    [Holder] nvarchar(256) NOT NULL,
    [Permission] nvarchar(256) NOT NULL,
    [Type] int NOT NULL,
    [Scope] nvarchar(300) NOT NULL,
    [ValidFrom] datetimeoffset NULL,
    [ValidUntil] datetimeoffset NULL,
    [Reason] nvarchar(1000) NULL,
    [CreatedBy] uniqueidentifier NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_ModulusScopedGrants] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID('ModulusSodRules') IS NULL
CREATE TABLE [ModulusSodRules] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [NormalizedName] nvarchar(200) NOT NULL,
    [Permissions] nvarchar(4000) NOT NULL,
    [Rationale] nvarchar(2000) NULL,
    [IsEnabled] bit NOT NULL,
    [CreatedBy] uniqueidentifier NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [UpdatedAt] datetimeoffset NULL,
    CONSTRAINT [PK_ModulusSodRules] PRIMARY KEY ([Id])
);
GO

IF OBJECT_ID('ModulusTenantPlans') IS NULL
CREATE TABLE [ModulusTenantPlans] (
    [TenantId] uniqueidentifier NOT NULL,
    [Plan] nvarchar(128) NOT NULL,
    CONSTRAINT [PK_ModulusTenantPlans] PRIMARY KEY ([TenantId])
);
GO

IF OBJECT_ID('ModulusRecertificationItems') IS NULL
CREATE TABLE [ModulusRecertificationItems] (
    [Id] uniqueidentifier NOT NULL,
    [TenantId] uniqueidentifier NOT NULL,
    [CampaignId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [Permission] nvarchar(max) NOT NULL,
    [Source] int NOT NULL,
    [Decision] int NOT NULL,
    [ReviewedAt] datetime2 NULL,
    [ReviewedBy] uniqueidentifier NULL,
    CONSTRAINT [PK_ModulusRecertificationItems] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ModulusRecertificationItems_ModulusRecertificationCampaigns_CampaignId] FOREIGN KEY ([CampaignId]) REFERENCES [ModulusRecertificationCampaigns] ([Id]) ON DELETE CASCADE
);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusAccessRequests_TenantId_Status_RequesterId' AND object_id = OBJECT_ID(N'ModulusAccessRequests'))
    CREATE INDEX IX_ModulusAccessRequests_TenantId_Status_RequesterId ON ModulusAccessRequests ([TenantId], [Status], [RequesterId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusApprovalAuthorities_TenantId_Permission_HolderType_Holder' AND object_id = OBJECT_ID(N'ModulusApprovalAuthorities'))
    CREATE INDEX IX_ModulusApprovalAuthorities_TenantId_Permission_HolderType_Holder ON ModulusApprovalAuthorities ([TenantId], [Permission], [HolderType], [Holder]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusApprovalChains_TenantId_DocumentType' AND object_id = OBJECT_ID(N'ModulusApprovalChains'))
    CREATE INDEX IX_ModulusApprovalChains_TenantId_DocumentType ON ModulusApprovalChains ([TenantId], [DocumentType]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusApprovalRequests_TenantId_DocumentType_DocumentId' AND object_id = OBJECT_ID(N'ModulusApprovalRequests'))
    CREATE INDEX IX_ModulusApprovalRequests_TenantId_DocumentType_DocumentId ON ModulusApprovalRequests ([TenantId], [DocumentType], [DocumentId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusApprovalRequests_TenantId_RequestedBy' AND object_id = OBJECT_ID(N'ModulusApprovalRequests'))
    CREATE INDEX IX_ModulusApprovalRequests_TenantId_RequestedBy ON ModulusApprovalRequests ([TenantId], [RequestedBy]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusApprovalRequests_TenantId_Status_CreatedAt' AND object_id = OBJECT_ID(N'ModulusApprovalRequests'))
    CREATE INDEX IX_ModulusApprovalRequests_TenantId_Status_CreatedAt ON ModulusApprovalRequests ([TenantId], [Status], [CreatedAt]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusAuthorizationAuditOutbox_ProcessedAt_NextAttemptAt' AND object_id = OBJECT_ID(N'ModulusAuthorizationAuditOutbox'))
    CREATE INDEX IX_ModulusAuthorizationAuditOutbox_ProcessedAt_NextAttemptAt ON ModulusAuthorizationAuditOutbox ([ProcessedAt], [NextAttemptAt]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusDataQualityFindings_CreatedAt' AND object_id = OBJECT_ID(N'ModulusDataQualityFindings'))
    CREATE INDEX IX_ModulusDataQualityFindings_CreatedAt ON ModulusDataQualityFindings ([CreatedAt]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusDataQualityFindings_TenantId_CreatedAt' AND object_id = OBJECT_ID(N'ModulusDataQualityFindings'))
    CREATE INDEX IX_ModulusDataQualityFindings_TenantId_CreatedAt ON ModulusDataQualityFindings ([TenantId], [CreatedAt]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusDelegations_TenantId_ToUserId' AND object_id = OBJECT_ID(N'ModulusDelegations'))
    CREATE INDEX IX_ModulusDelegations_TenantId_ToUserId ON ModulusDelegations ([TenantId], [ToUserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusOrgUnitProfiles_TenantId_NormalizedCode' AND object_id = OBJECT_ID(N'ModulusOrgUnitProfiles'))
    CREATE UNIQUE INDEX IX_ModulusOrgUnitProfiles_TenantId_NormalizedCode ON ModulusOrgUnitProfiles ([TenantId], [NormalizedCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusOrgUnits_TenantId' AND object_id = OBJECT_ID(N'ModulusOrgUnits'))
    CREATE INDEX IX_ModulusOrgUnits_TenantId ON ModulusOrgUnits ([TenantId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusPartyLinks_TenantId_Kind_PartyId' AND object_id = OBJECT_ID(N'ModulusPartyLinks'))
    CREATE INDEX IX_ModulusPartyLinks_TenantId_Kind_PartyId ON ModulusPartyLinks ([TenantId], [Kind], [PartyId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusPositionAssignments_TenantId_PositionId' AND object_id = OBJECT_ID(N'ModulusPositionAssignments'))
    CREATE INDEX IX_ModulusPositionAssignments_TenantId_PositionId ON ModulusPositionAssignments ([TenantId], [PositionId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusPositionAssignments_TenantId_UserId' AND object_id = OBJECT_ID(N'ModulusPositionAssignments'))
    CREATE INDEX IX_ModulusPositionAssignments_TenantId_UserId ON ModulusPositionAssignments ([TenantId], [UserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusPositions_TenantId_NormalizedCode' AND object_id = OBJECT_ID(N'ModulusPositions'))
    CREATE UNIQUE INDEX IX_ModulusPositions_TenantId_NormalizedCode ON ModulusPositions ([TenantId], [NormalizedCode]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusRecertificationCampaigns_TenantId_CompletedAt' AND object_id = OBJECT_ID(N'ModulusRecertificationCampaigns'))
    CREATE INDEX IX_ModulusRecertificationCampaigns_TenantId_CompletedAt ON ModulusRecertificationCampaigns ([TenantId], [CompletedAt]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusRecertificationItems_CampaignId_Decision' AND object_id = OBJECT_ID(N'ModulusRecertificationItems'))
    CREATE INDEX IX_ModulusRecertificationItems_CampaignId_Decision ON ModulusRecertificationItems ([CampaignId], [Decision]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusRecertificationItems_CampaignId_UserId' AND object_id = OBJECT_ID(N'ModulusRecertificationItems'))
    CREATE INDEX IX_ModulusRecertificationItems_CampaignId_UserId ON ModulusRecertificationItems ([CampaignId], [UserId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusRecertificationItems_TenantId_CampaignId' AND object_id = OBJECT_ID(N'ModulusRecertificationItems'))
    CREATE INDEX IX_ModulusRecertificationItems_TenantId_CampaignId ON ModulusRecertificationItems ([TenantId], [CampaignId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusCompanyAuthorityGrants_Lookup' AND object_id = OBJECT_ID(N'ModulusCompanyAuthorityGrants'))
    CREATE INDEX IX_ModulusCompanyAuthorityGrants_Lookup ON ModulusCompanyAuthorityGrants ([ParentId], [ChildId], [PrincipalId], [Operation]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusRecordShares_TenantId_ResourceType_ResourceId' AND object_id = OBJECT_ID(N'ModulusRecordShares'))
    CREATE INDEX IX_ModulusRecordShares_TenantId_ResourceType_ResourceId ON ModulusRecordShares ([TenantId], [ResourceType], [ResourceId]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusScopedGrants_TenantId_HolderType_Holder_Permission_Type_Scope' AND object_id = OBJECT_ID(N'ModulusScopedGrants'))
    CREATE UNIQUE INDEX IX_ModulusScopedGrants_TenantId_HolderType_Holder_Permission_Type_Scope ON ModulusScopedGrants ([TenantId], [HolderType], [Holder], [Permission], [Type], [Scope]);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ModulusSodRules_TenantId_NormalizedName' AND object_id = OBJECT_ID(N'ModulusSodRules'))
    CREATE UNIQUE INDEX IX_ModulusSodRules_TenantId_NormalizedName ON ModulusSodRules ([TenantId], [NormalizedName]);
GO

COMMIT TRANSACTION;
GO
