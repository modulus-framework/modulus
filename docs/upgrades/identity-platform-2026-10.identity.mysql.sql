-- Upgrade for existing MySQL deployments, IDENTITY database: the account lifecycle, principal kind, ERP identifiers and the lifecycle
-- history of 2026-10 (VER-2).
--
-- Run as the schema owner, once, before the new release starts. The user table is the application's identity user table (ASP.NET
-- Identity's default is AspNetUsers):
--   mysql --init-command="SET @user_table='AspNetUsers'" -h "$HOST" -u "$OWNER" -p "$DATABASE" < identity-platform-2026-10.identity.mysql.sql
--
-- Accounts disabled through IsActive = 0 are carried over to the Disabled lifecycle state, so they stay disabled. Only accounts whose
-- lifecycle has never changed are carried over, so a rerun cannot disable an account the new release has since re-enabled.
-- The ERP identifier columns are varchar(255), the length EF Core gives an indexed string on MySQL.
-- Idempotent: a second run changes nothing. Existing deployments only.

DROP PROCEDURE IF EXISTS modulus_upgrade_add_column;
DROP PROCEDURE IF EXISTS modulus_upgrade_add_index;

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

DELIMITER ;

CALL modulus_upgrade_add_column(@user_table, 'PrincipalKind', "varchar(32) NOT NULL DEFAULT 'person'");
CALL modulus_upgrade_add_column(@user_table, 'Lifecycle', "varchar(32) NOT NULL DEFAULT 'Active'");
CALL modulus_upgrade_add_column(@user_table, 'LifecycleChangedAt', 'datetime NULL');
CALL modulus_upgrade_add_column(@user_table, 'SuspensionReason', 'varchar(64) NULL');
CALL modulus_upgrade_add_column(@user_table, 'SuspensionNote', 'varchar(512) NULL');
CALL modulus_upgrade_add_column(@user_table, 'MustChangePin', 'tinyint(1) NOT NULL DEFAULT 0');
CALL modulus_upgrade_add_column(@user_table, 'EmployeeCode', 'varchar(255) NULL');
CALL modulus_upgrade_add_column(@user_table, 'PinHash', 'longtext NULL');
CALL modulus_upgrade_add_column(@user_table, 'ExternalId', 'varchar(255) NULL');

SET @backfill = CONCAT('UPDATE `', @user_table, '` SET Lifecycle = ''Disabled'' ',
                       'WHERE IsActive = 0 AND Lifecycle = ''Active'' AND LifecycleChangedAt IS NULL');
PREPARE stmt FROM @backfill;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

CALL modulus_upgrade_add_index(@user_table, 'IX_AspNetUsers_TenantId_EmployeeCode', '`TenantId`, `EmployeeCode`', FALSE);
CALL modulus_upgrade_add_index(@user_table, 'IX_AspNetUsers_TenantId_ExternalId', '`TenantId`, `ExternalId`', FALSE);

CREATE TABLE IF NOT EXISTS ModulusAccountTransitions (
    Id bigint NOT NULL AUTO_INCREMENT,
    UserId char(36) NOT NULL,
    FromState varchar(32) NOT NULL,
    ToState varchar(32) NOT NULL,
    ActorId char(36) NULL,
    Reason varchar(512) NULL,
    OccurredAt datetime NOT NULL,
    PRIMARY KEY (Id),
    KEY IX_ModulusAccountTransitions_UserId_Id (UserId, Id)
);

DROP PROCEDURE modulus_upgrade_add_column;
DROP PROCEDURE modulus_upgrade_add_index;
