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

DROP PROCEDURE modulus_upgrade_add_column;
DROP PROCEDURE modulus_upgrade_add_index;
DROP PROCEDURE modulus_upgrade_catalog_key;
