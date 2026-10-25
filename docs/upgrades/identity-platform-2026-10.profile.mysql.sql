-- Upgrade for existing MySQL deployments, IDENTITY database: the profile details of premodule 3 (work e-mail, job title, department,
-- primary branch, profile photo, pending work e-mail confirmation) and a company-scoped employee code.
--
-- Run as the schema owner, once, before the new release starts:
--   mysql --init-command="SET @user_table='AspNetUsers'" -h "$HOST" -u "$OWNER" -p "$DATABASE" < identity-platform-2026-10.profile.mysql.sql
--
-- Every new column is nullable, except the confirmation failure counter, which starts at 0. MySQL allows any number of NULLs in a unique
-- index, so accounts without a code do not conflict.
--
-- Refusal, not repair: MySQL does not roll back DDL, so the duplicate check runs first. If two accounts in one company already share an
-- employee code, the script stops with an error before it changes anything and nothing is deleted or rewritten. Resolve the duplicates,
-- then rerun.
--
-- Idempotent. Existing deployments only.

DROP PROCEDURE IF EXISTS modulus_upgrade_refuse_duplicate_codes;
DROP PROCEDURE IF EXISTS modulus_upgrade_add_column;
DROP PROCEDURE IF EXISTS modulus_upgrade_add_index;
DROP PROCEDURE IF EXISTS modulus_upgrade_drop_index;

DELIMITER //

CREATE PROCEDURE modulus_upgrade_refuse_duplicate_codes(IN tbl VARCHAR(64))
BEGIN
    SET @ddl = CONCAT('SELECT COUNT(*) INTO @modulus_dup_count FROM (SELECT 1 FROM `', tbl,
                      '` WHERE EmployeeCode IS NOT NULL GROUP BY TenantId, EmployeeCode HAVING COUNT(*) > 1) d');
    PREPARE stmt FROM @ddl;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;
    IF @modulus_dup_count > 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'REFUSED: duplicate employee codes in one company. Nothing changed. See the release notes.';
    END IF;
END //

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

CREATE PROCEDURE modulus_upgrade_drop_index(IN tbl VARCHAR(64), IN idx VARCHAR(64))
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.STATISTICS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND INDEX_NAME = idx) THEN
        SET @ddl = CONCAT('DROP INDEX `', idx, '` ON `', tbl, '`');
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END //

DELIMITER ;

CALL modulus_upgrade_refuse_duplicate_codes(@user_table);

CALL modulus_upgrade_add_column(@user_table, 'WorkEmail', 'varchar(256) NULL');
CALL modulus_upgrade_add_column(@user_table, 'JobTitle', 'varchar(100) NULL');
CALL modulus_upgrade_add_column(@user_table, 'Department', 'varchar(100) NULL');
CALL modulus_upgrade_add_column(@user_table, 'PrimaryBranchId', 'char(36) NULL');
CALL modulus_upgrade_add_column(@user_table, 'PhotoAssetId', 'char(36) NULL');
CALL modulus_upgrade_add_column(@user_table, 'PendingWorkEmail', 'varchar(256) NULL');
CALL modulus_upgrade_add_column(@user_table, 'WorkEmailVerificationStamp', 'varchar(64) NULL');
CALL modulus_upgrade_add_column(@user_table, 'WorkEmailRequestedAt', 'datetime NULL');
CALL modulus_upgrade_add_column(@user_table, 'ConfirmationFailures', 'int NOT NULL DEFAULT 0');

-- The unique index is created before the old index is dropped: if the create fails, the old index is still there.
CALL modulus_upgrade_add_index(@user_table, 'IX_AspNetUsers_TenantId_EmployeeCode_Unique', '`TenantId`, `EmployeeCode`', TRUE);
CALL modulus_upgrade_drop_index(@user_table, 'IX_AspNetUsers_TenantId_EmployeeCode');

DROP PROCEDURE IF EXISTS modulus_upgrade_refuse_duplicate_codes;
DROP PROCEDURE IF EXISTS modulus_upgrade_add_column;
DROP PROCEDURE IF EXISTS modulus_upgrade_add_index;
DROP PROCEDURE IF EXISTS modulus_upgrade_drop_index;
