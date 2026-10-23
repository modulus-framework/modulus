-- Upgrade for existing MySQL deployments, TENANCY database: the identity platform changes of 2026-10.
--
-- Run as the schema owner, never as an application user, once, before the new release starts:
--   mysql -h "$HOST" -u "$OWNER" -p "$DATABASE" < identity-platform-2026-10.tenancy.mysql.sql
--
-- Adds the company lifecycle columns (Status, TrialEndsAt, MaxUsers) to ModulusTenants and creates ModulusTenantDataStores and ModulusSettingValues.
-- MySQL has no ADD COLUMN IF NOT EXISTS, so the helper procedure checks information_schema first. It is dropped at the end.
-- Idempotent: a second run changes nothing. Existing companies start as Active with no trial end and no seat limit.
-- Existing deployments only: it expects ModulusTenants to exist.

DROP PROCEDURE IF EXISTS modulus_upgrade_add_column;

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

DELIMITER ;

-- 1. Company lifecycle. Status is the TenantStatus enum stored as its integer value (Trial 0, Active 1, Suspended 2, Closed 3).
CALL modulus_upgrade_add_column('ModulusTenants', 'Status', 'int NOT NULL DEFAULT 1');
CALL modulus_upgrade_add_column('ModulusTenants', 'TrialEndsAt', 'datetime NULL');
CALL modulus_upgrade_add_column('ModulusTenants', 'MaxUsers', 'int NULL');

-- 2. Tenant data stores: a pointer to a company's database secret, never a connection string.
CREATE TABLE IF NOT EXISTS ModulusTenantDataStores (
    TenantId char(36) NOT NULL,
    StoreKey varchar(32) NOT NULL,
    Isolation varchar(32) NOT NULL,
    ProviderKey varchar(32) NOT NULL,
    SecretPath varchar(256) NOT NULL,
    SecretVersion int NOT NULL DEFAULT 0,
    Status varchar(32) NOT NULL,
    PRIMARY KEY (TenantId, StoreKey)
);

-- 3. Setting values: one row per setting and scope (installation, company or person). ScopeKey is a non-null text column, so
--    (Name, ScopeKey) is unique at every scope, the installation's included, on every engine.
CREATE TABLE IF NOT EXISTS ModulusSettingValues (
    Name varchar(128) NOT NULL,
    ScopeKey varchar(80) NOT NULL,
    TenantId char(36) NULL,
    UserId char(36) NULL,
    Value varchar(4000) NOT NULL,
    PRIMARY KEY (Name, ScopeKey)
);

-- 4. Settings change counter: every settings write advances it, and the other nodes check it on a schedule, so a change reaches them
--    without Redis. The first write creates the row.
CREATE TABLE IF NOT EXISTS ModulusSettingVersions (
    Name varchar(64) NOT NULL,
    Version bigint NOT NULL,
    PRIMARY KEY (Name)
);

DROP PROCEDURE IF EXISTS modulus_upgrade_add_column;
