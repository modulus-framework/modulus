-- Upgrade for existing PostgreSQL deployments, AUDIT database: the security audit tables added in 2026-10 (A11-1, A11-2).
--
-- Run as the schema owner, once, before the new release starts:
--   psql "$AUDIT_CONNECTION" -v ON_ERROR_STOP=1 -f identity-platform-2026-10.audit.postgresql.sql
--
-- Afterwards, re-apply the purge and append-only grants and triggers for the new tables with SecurityAuditDatabaseScripts.PostgreSql,
-- so the application role can read and insert archive rows and holds, and only the purge role can purge (review the script first).
-- Idempotent: a second run changes nothing.
-- The security audit hashes whole milliseconds (SecurityAuditChain.Normalize), so every stored OccurredAt already has no finer precision:
-- narrowing the column to timestamp(3), as EF maps it, keeps every value. Section 4 does that.

BEGIN;

-- Tables.
CREATE TABLE IF NOT EXISTS modulus_security_audit_checkpoints (
    "ChainId" uuid NOT NULL,
    "Sequence" bigint NOT NULL,
    "Hash" char(64) NOT NULL,
    "PurgedAtUtc" timestamptz NOT NULL,
    CONSTRAINT "PK_modulus_security_audit_checkpoints" PRIMARY KEY ("ChainId")
);

CREATE TABLE IF NOT EXISTS modulus_security_audit_holds (
    "ChainId" uuid NOT NULL,
    "Reason" varchar(500) NOT NULL,
    "PlacedAtUtc" timestamptz NOT NULL,
    CONSTRAINT "PK_modulus_security_audit_holds" PRIMARY KEY ("ChainId")
);

CREATE TABLE IF NOT EXISTS modulus_security_audit_archive (
    "ChainId" uuid NOT NULL,
    "Sequence" bigint NOT NULL,
    "PreviousHash" char(64) NOT NULL,
    "Hash" char(64) NOT NULL,
    "Payload" text NOT NULL,
    "ArchivedAtUtc" timestamptz NOT NULL,
    CONSTRAINT "PK_modulus_security_audit_archive" PRIMARY KEY ("ChainId", "Sequence")
);


-- 4. modulus_security_audit.OccurredAt: EF maps it as timestamp(3). Narrowing is lossless because the stored values are whole milliseconds.
ALTER TABLE IF EXISTS modulus_security_audit ALTER COLUMN "OccurredAt" TYPE timestamp(3) with time zone;

COMMIT;
