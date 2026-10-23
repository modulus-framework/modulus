-- Upgrade for existing MySQL deployments, AUDIT database: the security audit tables added in 2026-10 (A11-1, A11-2).
--
-- Run as the schema owner, once, before the new release starts:
--   mysql -h "$HOST" -u "$OWNER" -p "$DATABASE" < identity-platform-2026-10.audit.mysql.sql
--
-- Afterwards re-apply the purge and append-only grants and triggers for the new tables with SecurityAuditDatabaseScripts.MySql, so the
-- application user can read and insert archive rows and holds, and only the purge user can purge (review the script first).
-- Idempotent: a second run changes nothing.
-- BEFORE RUNNING: the existing modulus_security_audit.OccurredAt values were written as datetime (whole seconds, UTC). Their milliseconds
-- are already lost, so the hashes of those entries cannot be verified again after this step. Verify against a known event time that the
-- stored values are UTC, and record the pre-upgrade chain heads (AuditAnchorService sinks) as the proof of the entries before the break.


CREATE TABLE IF NOT EXISTS modulus_security_audit_checkpoints (
    ChainId char(36) NOT NULL,
    `Sequence` bigint NOT NULL,
    Hash char(64) NOT NULL,
    PurgedAtUtc datetime NOT NULL,
    PRIMARY KEY (ChainId)
);

CREATE TABLE IF NOT EXISTS modulus_security_audit_holds (
    ChainId char(36) NOT NULL,
    Reason varchar(500) NOT NULL,
    PlacedAtUtc datetime NOT NULL,
    PRIMARY KEY (ChainId)
);

CREATE TABLE IF NOT EXISTS modulus_security_audit_archive (
    ChainId char(36) NOT NULL,
    `Sequence` bigint NOT NULL,
    PreviousHash char(64) NOT NULL,
    Hash char(64) NOT NULL,
    Payload longtext NOT NULL,
    ArchivedAtUtc datetime NOT NULL,
    PRIMARY KEY (ChainId, `Sequence`)
);

-- 4. modulus_security_audit.OccurredAt: Unix milliseconds in a bigint, as EF maps it on MySQL. Runs only while the column is still datetime.
SET time_zone = '+00:00';
DROP PROCEDURE IF EXISTS modulus_upgrade_occurred_at;
DELIMITER //
CREATE PROCEDURE modulus_upgrade_occurred_at()
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.COLUMNS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'modulus_security_audit'
                 AND COLUMN_NAME = 'OccurredAt' AND DATA_TYPE = 'datetime') THEN
        ALTER TABLE modulus_security_audit ADD COLUMN OccurredAt_ms bigint NULL;
        UPDATE modulus_security_audit SET OccurredAt_ms = UNIX_TIMESTAMP(OccurredAt) * 1000;
        ALTER TABLE modulus_security_audit DROP COLUMN OccurredAt;
        ALTER TABLE modulus_security_audit CHANGE OccurredAt_ms OccurredAt bigint NOT NULL;
    END IF;
END //
DELIMITER ;
CALL modulus_upgrade_occurred_at();
DROP PROCEDURE modulus_upgrade_occurred_at;

