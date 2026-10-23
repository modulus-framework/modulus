-- Upgrade for existing MySQL deployments, AUTHORIZATION database: the identity platform changes of 2026-10 (VER-2).
--
-- Run as the schema owner, never as an application user, once, before the new release starts. The api owner is passed as a variable:
--   mysql --init-command="SET @api_owner='MyApp.Api'" -h "$HOST" -u "$OWNER" -p "$DATABASE" < identity-platform-2026-10.authorization.mysql.sql
--
-- @api_owner is the owner of the permission catalog rows recorded so far: the API host's assembly name (PermissionCatalogOptions.Owner).
-- MySQL has no ADD COLUMN IF NOT EXISTS, so the helper procedures below check information_schema first. They are dropped at the end.
-- Idempotent: a second run changes nothing. Existing deployments only: it expects the catalog and grant tables to exist.

DROP PROCEDURE IF EXISTS modulus_upgrade_add_column;
DROP PROCEDURE IF EXISTS modulus_upgrade_add_index;
DROP PROCEDURE IF EXISTS modulus_upgrade_catalog_key;

DELIMITER //

CREATE PROCEDURE modulus_upgrade_add_column(IN tbl VARCHAR(64), IN col VARCHAR(64), IN definition TEXT)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS
                   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = col) THEN
        SET @ddl = CONCAT('ALTER TABLE `', tbl, '` ADD COLUMN `', col, '` ', definition);
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END //

CREATE PROCEDURE modulus_upgrade_add_index(IN tbl VARCHAR(64), IN idx VARCHAR(64), IN cols TEXT, IN is_unique BOOLEAN)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS
                   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND INDEX_NAME = idx) THEN
        SET @ddl = CONCAT('CREATE ', IF(is_unique, 'UNIQUE ', ''), 'INDEX `', idx, '` ON `', tbl, '` (', cols, ')');
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END //

-- The catalog's primary key becomes (Owner, Permission). Runs once: a key that already starts with Owner is left alone.
CREATE PROCEDURE modulus_upgrade_catalog_key()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.KEY_COLUMN_USAGE
                   WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ModulusPermissionCatalog'
                     AND CONSTRAINT_NAME = 'PRIMARY' AND COLUMN_NAME = 'Owner'
                     AND ORDINAL_POSITION = 1) THEN
        ALTER TABLE ModulusPermissionCatalog DROP PRIMARY KEY, ADD PRIMARY KEY (Owner, Permission);
    END IF;
END //

DELIMITER ;

-- 1. Permission catalog: one set of rows per host.
CALL modulus_upgrade_add_column('ModulusPermissionCatalog', 'Owner', 'varchar(128) NULL');
UPDATE ModulusPermissionCatalog SET Owner = @api_owner WHERE Owner IS NULL;
ALTER TABLE ModulusPermissionCatalog MODIFY Owner varchar(128) NOT NULL;
CALL modulus_upgrade_catalog_key();

-- 2. First-boot grant seed marker. A database that already holds grants is marked as seeded, so an upgrade never re-applies the seed
--    over grants an administrator has changed. An empty database is seeded on its first boot.
CREATE TABLE IF NOT EXISTS ModulusAuthorizationSeeds (
    Name varchar(128) NOT NULL,
    AppliedAtUtc datetime NOT NULL,
    PRIMARY KEY (Name)
);
INSERT IGNORE INTO ModulusAuthorizationSeeds (Name, AppliedAtUtc)
SELECT 'permission-grants', UTC_TIMESTAMP()
FROM DUAL
WHERE EXISTS (SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ModulusPermissionGrants')
  AND EXISTS (SELECT 1 FROM ModulusPermissionGrants);

-- 3. Dimension values (scope codes per company). A code is unique within its dimension and company.
CREATE TABLE IF NOT EXISTS ModulusDimensionValues (
    TenantId char(36) NOT NULL,
    Id char(36) NOT NULL,
    Dimension varchar(64) NOT NULL,
    Code varchar(64) NOT NULL,
    Label varchar(128) NOT NULL,
    ParentId char(36) NULL,
    PRIMARY KEY (TenantId, Id)
);
CALL modulus_upgrade_add_index('ModulusDimensionValues', 'IX_ModulusDimensionValues_TenantId_Dimension_Code', '`TenantId`, `Dimension`, `Code`', TRUE);
CALL modulus_upgrade_add_index('ModulusDimensionValues', 'IX_ModulusDimensionValues_TenantId_ParentId', '`TenantId`, `ParentId`', FALSE);

-- 4. Permission exceptions: the register of privileged-permission exceptions (who keeps a Critical permission, why, until when).
CREATE TABLE IF NOT EXISTS ModulusPermissionExceptions (
    Id char(36) NOT NULL,
    TenantId char(36) NOT NULL,
    Permission varchar(200) NOT NULL,
    HolderType int NOT NULL,
    Holder varchar(200) NOT NULL,
    Justification varchar(2000) NOT NULL,
    CompensatingControl varchar(2000) NULL,
    BusinessOwner varchar(200) NOT NULL,
    RequestedBy char(36) NOT NULL,
    ValidFrom datetime NOT NULL,
    ValidUntil datetime NOT NULL,
    ReviewDueOn datetime NOT NULL,
    CreatedAt datetime NOT NULL,
    ReviewedBy char(36) NULL,
    ReviewedAt datetime NULL,
    RevokedBy char(36) NULL,
    RevokedAt datetime NULL,
    PRIMARY KEY (Id)
);
CALL modulus_upgrade_add_index('ModulusPermissionExceptions', 'IX_PermissionExceptions_Holder', '`TenantId`, `Permission`, `HolderType`, `Holder`', FALSE);

-- 5. The remaining authorization tables: every table of the authorization store that the steps above do not create (generated from the
--    EF model, 2026-10-23). Each statement is idempotent: an existing table or index is left as it is.
CREATE TABLE IF NOT EXISTS `ModulusAccessRequests` (
    `Id` char(36) NOT NULL,
    `TenantId` char(36) NOT NULL,
    `Kind` int NOT NULL,
    `RequesterId` char(36) NOT NULL,
    `Permissions` varchar(4000) NOT NULL,
    `Reason` varchar(2000) NOT NULL,
    `Hours` int NOT NULL,
    `Status` int NOT NULL,
    `CreatedAt` datetime NOT NULL,
    `DecidedBy` char(36) NULL,
    `DecidedAt` datetime NULL,
    `Note` varchar(2000) NULL,
    `AccessEndsAt` datetime NULL,
    `ReviewedBy` char(36) NULL,
    `ReviewedAt` datetime NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE IF NOT EXISTS `ModulusApprovalAuthorities` (
    `Id` char(36) NOT NULL,
    `TenantId` char(36) NOT NULL,
    `HolderType` int NOT NULL,
    `Holder` varchar(256) NOT NULL,
    `Permission` varchar(256) NOT NULL,
    `MaxAmount` decimal(18,4) NOT NULL,
    `Currency` varchar(8) NULL,
    `DocumentType` varchar(256) NULL,
    `OrgUnitId` char(36) NULL,
    `ValidFrom` datetime NULL,
    `ValidUntil` datetime NULL,
    `CreatedBy` char(36) NULL,
    `CreatedAt` datetime NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE IF NOT EXISTS `ModulusApprovalChains` (
    `Id` char(36) NOT NULL,
    `TenantId` char(36) NOT NULL,
    `Name` varchar(256) NOT NULL,
    `DocumentType` varchar(256) NOT NULL,
    `MinAmount` decimal(18,4) NULL,
    `MaxAmount` decimal(18,4) NULL,
    `Currency` varchar(8) NULL,
    `Steps` longtext NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE IF NOT EXISTS `ModulusApprovalRequests` (
    `Id` char(36) NOT NULL,
    `TenantId` char(36) NOT NULL,
    `ChainId` char(36) NOT NULL,
    `DocumentType` varchar(256) NOT NULL,
    `DocumentId` varchar(256) NOT NULL,
    `Amount` decimal(18,4) NOT NULL,
    `Currency` varchar(8) NULL,
    `OrgUnitId` char(36) NULL,
    `RequestedBy` char(36) NOT NULL,
    `Summary` varchar(1000) NULL,
    `Steps` longtext NOT NULL,
    `CurrentStep` int NOT NULL,
    `Status` int NOT NULL,
    `Decisions` longtext NOT NULL,
    `CreatedAt` datetime NOT NULL,
    `StepStartedAt` datetime NOT NULL,
    `Escalated` tinyint(1) NOT NULL,
    `Version` bigint NOT NULL,
    `CompletedAt` datetime NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE IF NOT EXISTS `ModulusAssignments` (
    `TenantId` char(36) NOT NULL,
    `UserId` char(36) NOT NULL,
    `AssignmentType` varchar(128) NOT NULL,
    `TargetId` char(36) NOT NULL,
    `ValidFrom` datetime NULL,
    `ValidUntil` datetime NULL,
    PRIMARY KEY (`TenantId`, `UserId`, `AssignmentType`, `TargetId`)
);

CREATE TABLE IF NOT EXISTS `ModulusAuthorizationAuditOutbox` (
    `Id` char(36) NOT NULL,
    `MessageType` varchar(256) NOT NULL,
    `Payload` longtext NOT NULL,
    `TenantId` char(36) NOT NULL,
    `ModuleName` longtext NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `ProcessedAt` datetime(6) NULL,
    `LockedBy` longtext NULL,
    `LockedUntil` datetime(6) NULL,
    `NextAttemptAt` datetime(6) NULL,
    `RetryCount` int NOT NULL,
    `Error` longtext NULL,
    `CorrelationId` longtext NULL,
    `CausationId` longtext NULL,
    `TraceParent` longtext NULL,
    `TraceState` longtext NULL,
    `SchemaVersion` int NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE IF NOT EXISTS `ModulusCompanyProfiles` (
    `TenantId` char(36) NOT NULL,
    `LegalName` varchar(256) NOT NULL,
    `TradeName` varchar(256) NULL,
    `RegistrationNumber` varchar(64) NULL,
    `TaxId` varchar(64) NULL,
    `Address` varchar(1000) NULL,
    `Country` varchar(2) NULL,
    `Currency` varchar(3) NULL,
    `FiscalYearStartMonth` int NOT NULL,
    `TimeZone` varchar(64) NULL,
    `Language` varchar(16) NULL,
    `UpdatedAt` datetime NOT NULL,
    PRIMARY KEY (`TenantId`)
);

CREATE TABLE IF NOT EXISTS `ModulusDataQualityFindings` (
    `Id` char(36) NOT NULL,
    `TenantId` char(36) NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `ResourceType` longtext NOT NULL,
    `RecordId` longtext NULL,
    `Action` longtext NOT NULL,
    `MissingAttributes` longtext NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE IF NOT EXISTS `ModulusDelegations` (
    `Id` char(36) NOT NULL,
    `TenantId` char(36) NOT NULL,
    `FromUserId` char(36) NOT NULL,
    `FromRolesJson` longtext NOT NULL,
    `ToUserId` char(36) NOT NULL,
    `PermissionsJson` longtext NOT NULL,
    `NotBefore` datetime NOT NULL,
    `NotAfter` datetime NOT NULL,
    `Revoked` tinyint(1) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE IF NOT EXISTS `ModulusFeatureOverrides` (
    `TenantId` char(36) NOT NULL,
    `Feature` varchar(256) NOT NULL,
    `Enabled` tinyint(1) NOT NULL,
    PRIMARY KEY (`TenantId`, `Feature`)
);

CREATE TABLE IF NOT EXISTS `ModulusOrgPlacements` (
    `TenantId` char(36) NOT NULL,
    `UserId` char(36) NOT NULL,
    `OrgUnitId` char(36) NOT NULL,
    `Mode` int NOT NULL,
    PRIMARY KEY (`TenantId`, `UserId`, `OrgUnitId`)
);

CREATE TABLE IF NOT EXISTS `ModulusOrgUnitParents` (
    `TenantId` char(36) NOT NULL,
    `ChildId` char(36) NOT NULL,
    `ParentId` char(36) NOT NULL,
    PRIMARY KEY (`TenantId`, `ChildId`, `ParentId`)
);

CREATE TABLE IF NOT EXISTS `ModulusOrgUnitProfiles` (
    `TenantId` char(36) NOT NULL,
    `UnitId` char(36) NOT NULL,
    `Code` varchar(64) NOT NULL,
    `NormalizedCode` varchar(64) NOT NULL,
    `Name` varchar(256) NOT NULL,
    `Kind` varchar(64) NOT NULL,
    `IsClosed` tinyint(1) NOT NULL,
    `ManagerUserId` char(36) NULL,
    `ClosedAt` datetime NULL,
    `UpdatedAt` datetime NOT NULL,
    PRIMARY KEY (`TenantId`, `UnitId`)
);

CREATE TABLE IF NOT EXISTS `ModulusOrgUnits` (
    `Id` char(36) NOT NULL,
    `TenantId` char(36) NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE IF NOT EXISTS `ModulusPartyCeilings` (
    `TenantId` char(36) NOT NULL,
    `Kind` varchar(64) NOT NULL,
    `Permission` varchar(256) NOT NULL,
    PRIMARY KEY (`TenantId`, `Kind`, `Permission`)
);

CREATE TABLE IF NOT EXISTS `ModulusPartyLinks` (
    `TenantId` char(36) NOT NULL,
    `UserId` char(36) NOT NULL,
    `Kind` varchar(64) NOT NULL,
    `PartyId` char(36) NOT NULL,
    `CreatedBy` char(36) NULL,
    `CreatedAt` datetime NOT NULL,
    PRIMARY KEY (`TenantId`, `UserId`)
);

CREATE TABLE IF NOT EXISTS `ModulusPlanFeatures` (
    `Plan` varchar(128) NOT NULL,
    `Feature` varchar(256) NOT NULL,
    PRIMARY KEY (`Plan`, `Feature`)
);

CREATE TABLE IF NOT EXISTS `ModulusPositionAssignments` (
    `Id` char(36) NOT NULL,
    `TenantId` char(36) NOT NULL,
    `PositionId` char(36) NOT NULL,
    `UserId` char(36) NOT NULL,
    `ValidFrom` datetime NOT NULL,
    `ValidUntil` datetime NULL,
    `CreatedBy` char(36) NULL,
    `CreatedAt` datetime NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE IF NOT EXISTS `ModulusPositions` (
    `Id` char(36) NOT NULL,
    `TenantId` char(36) NOT NULL,
    `Code` varchar(100) NOT NULL,
    `NormalizedCode` varchar(100) NOT NULL,
    `Name` varchar(200) NOT NULL,
    `OrgUnitId` char(36) NULL,
    `Roles` varchar(4000) NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreatedAt` datetime NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE IF NOT EXISTS `ModulusRecertificationCampaigns` (
    `Id` char(36) NOT NULL,
    `TenantId` char(36) NOT NULL,
    `Name` longtext NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `CreatedBy` char(36) NOT NULL,
    `CompletedAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE IF NOT EXISTS `ModulusRecordShares` (
    `Id` char(36) NOT NULL,
    `TenantId` char(36) NOT NULL,
    `ResourceType` varchar(256) NOT NULL,
    `ResourceId` varchar(256) NOT NULL,
    `GranteeUserId` char(36) NOT NULL,
    `SharedByUserId` char(36) NOT NULL,
    `CreatedAt` datetime NOT NULL,
    `ExpiresAt` datetime NOT NULL,
    `RevokedAt` datetime NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE IF NOT EXISTS `ModulusRoleInclusions` (
    `TenantId` char(36) NOT NULL,
    `NormalizedRole` varchar(256) NOT NULL,
    `NormalizedIncludes` varchar(256) NOT NULL,
    `Role` varchar(256) NOT NULL,
    `Includes` varchar(256) NOT NULL,
    `CreatedBy` char(36) NULL,
    `CreatedAt` datetime NOT NULL,
    PRIMARY KEY (`TenantId`, `NormalizedRole`, `NormalizedIncludes`)
);

CREATE TABLE IF NOT EXISTS `ModulusScopedGrants` (
    `Id` char(36) NOT NULL,
    `TenantId` char(36) NOT NULL,
    `HolderType` int NOT NULL,
    `Holder` varchar(256) NOT NULL,
    `Permission` varchar(256) NOT NULL,
    `Type` int NOT NULL,
    `Scope` varchar(200) NOT NULL,
    `ValidFrom` datetime NULL,
    `ValidUntil` datetime NULL,
    `Reason` varchar(1000) NULL,
    `CreatedBy` char(36) NULL,
    `CreatedAt` datetime NOT NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE IF NOT EXISTS `ModulusSodRules` (
    `Id` char(36) NOT NULL,
    `TenantId` char(36) NOT NULL,
    `Name` varchar(200) NOT NULL,
    `NormalizedName` varchar(200) NOT NULL,
    `Permissions` varchar(4000) NOT NULL,
    `Rationale` varchar(2000) NULL,
    `IsEnabled` tinyint(1) NOT NULL,
    `CreatedBy` char(36) NULL,
    `CreatedAt` datetime NOT NULL,
    `UpdatedAt` datetime NULL,
    PRIMARY KEY (`Id`)
);

CREATE TABLE IF NOT EXISTS `ModulusTenantPlans` (
    `TenantId` char(36) NOT NULL,
    `Plan` varchar(128) NOT NULL,
    PRIMARY KEY (`TenantId`)
);

CREATE TABLE IF NOT EXISTS `ModulusRecertificationItems` (
    `Id` char(36) NOT NULL,
    `TenantId` char(36) NOT NULL,
    `CampaignId` char(36) NOT NULL,
    `UserId` char(36) NOT NULL,
    `Permission` longtext NOT NULL,
    `Source` int NOT NULL,
    `Decision` int NOT NULL,
    `ReviewedAt` datetime(6) NULL,
    `ReviewedBy` char(36) NULL,
    PRIMARY KEY (`Id`),
    CONSTRAINT `FK_ModulusRecertificationItems_ModulusRecertificationCampaigns_~` FOREIGN KEY (`CampaignId`) REFERENCES `ModulusRecertificationCampaigns` (`Id`) ON DELETE CASCADE
);

CALL modulus_upgrade_add_index('ModulusAccessRequests', 'IX_ModulusAccessRequests_TenantId_Status_RequesterId', '`TenantId`, `Status`, `RequesterId`', FALSE);

CALL modulus_upgrade_add_index('ModulusApprovalAuthorities', 'IX_ModulusApprovalAuthorities_TenantId_Permission_HolderType_Ho~', '`TenantId`, `Permission`, `HolderType`, `Holder`', FALSE);

CALL modulus_upgrade_add_index('ModulusApprovalChains', 'IX_ModulusApprovalChains_TenantId_DocumentType', '`TenantId`, `DocumentType`', FALSE);

CALL modulus_upgrade_add_index('ModulusApprovalRequests', 'IX_ModulusApprovalRequests_TenantId_DocumentType_DocumentId', '`TenantId`, `DocumentType`, `DocumentId`', FALSE);

CALL modulus_upgrade_add_index('ModulusApprovalRequests', 'IX_ModulusApprovalRequests_TenantId_RequestedBy', '`TenantId`, `RequestedBy`', FALSE);

CALL modulus_upgrade_add_index('ModulusApprovalRequests', 'IX_ModulusApprovalRequests_TenantId_Status_CreatedAt', '`TenantId`, `Status`, `CreatedAt`', FALSE);

CALL modulus_upgrade_add_index('ModulusAuthorizationAuditOutbox', 'IX_ModulusAuthorizationAuditOutbox_ProcessedAt_NextAttemptAt', '`ProcessedAt`, `NextAttemptAt`', FALSE);

CALL modulus_upgrade_add_index('ModulusDataQualityFindings', 'IX_ModulusDataQualityFindings_CreatedAt', '`CreatedAt`', FALSE);

CALL modulus_upgrade_add_index('ModulusDataQualityFindings', 'IX_ModulusDataQualityFindings_TenantId_CreatedAt', '`TenantId`, `CreatedAt`', FALSE);

CALL modulus_upgrade_add_index('ModulusDelegations', 'IX_ModulusDelegations_TenantId_ToUserId', '`TenantId`, `ToUserId`', FALSE);

CALL modulus_upgrade_add_index('ModulusOrgUnitProfiles', 'IX_ModulusOrgUnitProfiles_TenantId_NormalizedCode', '`TenantId`, `NormalizedCode`', TRUE);

CALL modulus_upgrade_add_index('ModulusOrgUnits', 'IX_ModulusOrgUnits_TenantId', '`TenantId`', FALSE);

CALL modulus_upgrade_add_index('ModulusPartyLinks', 'IX_ModulusPartyLinks_TenantId_Kind_PartyId', '`TenantId`, `Kind`, `PartyId`', FALSE);

CALL modulus_upgrade_add_index('ModulusPositionAssignments', 'IX_ModulusPositionAssignments_TenantId_PositionId', '`TenantId`, `PositionId`', FALSE);

CALL modulus_upgrade_add_index('ModulusPositionAssignments', 'IX_ModulusPositionAssignments_TenantId_UserId', '`TenantId`, `UserId`', FALSE);

CALL modulus_upgrade_add_index('ModulusPositions', 'IX_ModulusPositions_TenantId_NormalizedCode', '`TenantId`, `NormalizedCode`', TRUE);

CALL modulus_upgrade_add_index('ModulusRecertificationCampaigns', 'IX_ModulusRecertificationCampaigns_TenantId_CompletedAt', '`TenantId`, `CompletedAt`', FALSE);

CALL modulus_upgrade_add_index('ModulusRecertificationItems', 'IX_ModulusRecertificationItems_CampaignId_Decision', '`CampaignId`, `Decision`', FALSE);

CALL modulus_upgrade_add_index('ModulusRecertificationItems', 'IX_ModulusRecertificationItems_CampaignId_UserId', '`CampaignId`, `UserId`', FALSE);

CALL modulus_upgrade_add_index('ModulusRecertificationItems', 'IX_ModulusRecertificationItems_TenantId_CampaignId', '`TenantId`, `CampaignId`', FALSE);

CALL modulus_upgrade_add_index('ModulusRecordShares', 'IX_ModulusRecordShares_TenantId_ResourceType_ResourceId', '`TenantId`, `ResourceType`, `ResourceId`', FALSE);

CALL modulus_upgrade_add_index('ModulusScopedGrants', 'IX_ModulusScopedGrants_TenantId_HolderType_Holder_Permission_Ty~', '`TenantId`, `HolderType`, `Holder`, `Permission`, `Type`, `Scope`', TRUE);

CALL modulus_upgrade_add_index('ModulusSodRules', 'IX_ModulusSodRules_TenantId_NormalizedName', '`TenantId`, `NormalizedName`', TRUE);

DROP PROCEDURE modulus_upgrade_add_column;
DROP PROCEDURE modulus_upgrade_add_index;
DROP PROCEDURE modulus_upgrade_catalog_key;
