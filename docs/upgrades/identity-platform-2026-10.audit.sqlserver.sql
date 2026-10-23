/*
  Upgrade for existing SQL Server deployments, AUDIT database: the security audit tables added in 2026-10 (A11-1, A11-2).

  Run as the schema owner, once, before the new release starts:
    sqlcmd -S "$SERVER" -d "$DATABASE" -U "$OWNER" -P "$OWNER_PASSWORD" -b -i identity-platform-2026-10.audit.sqlserver.sql

  Afterwards re-apply the purge and append-only grants and triggers for the new tables with SecurityAuditDatabaseScripts.SqlServer, so the
  application login can read and insert archive rows and holds, and only the purge user can purge (review the script first).
  The security audit hashes whole milliseconds, so every stored OccurredAt already has no finer precision: narrowing it to datetimeoffset(3),
  as EF maps it, keeps every value (the last step).
  Idempotent: a second run changes nothing.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;
GO

IF OBJECT_ID('modulus_security_audit_checkpoints') IS NULL
    CREATE TABLE modulus_security_audit_checkpoints (
        ChainId uniqueidentifier NOT NULL CONSTRAINT PK_modulus_security_audit_checkpoints PRIMARY KEY,
        Sequence bigint NOT NULL,
        Hash nchar(64) NOT NULL,
        PurgedAtUtc datetimeoffset NOT NULL);
GO

IF OBJECT_ID('modulus_security_audit_holds') IS NULL
    CREATE TABLE modulus_security_audit_holds (
        ChainId uniqueidentifier NOT NULL CONSTRAINT PK_modulus_security_audit_holds PRIMARY KEY,
        Reason nvarchar(500) NOT NULL,
        PlacedAtUtc datetimeoffset NOT NULL);
GO

IF OBJECT_ID('modulus_security_audit_archive') IS NULL
    CREATE TABLE modulus_security_audit_archive (
        ChainId uniqueidentifier NOT NULL,
        Sequence bigint NOT NULL,
        PreviousHash nchar(64) NOT NULL,
        Hash nchar(64) NOT NULL,
        Payload nvarchar(max) NOT NULL,
        ArchivedAtUtc datetimeoffset NOT NULL,
        CONSTRAINT PK_modulus_security_audit_archive PRIMARY KEY (ChainId, Sequence));
GO

-- The security audit's OccurredAt is whole milliseconds, which datetimeoffset(3) holds exactly.
IF EXISTS (SELECT 1 FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
           WHERE c.object_id = OBJECT_ID('modulus_security_audit') AND c.name = 'OccurredAt' AND t.name = 'datetimeoffset' AND c.scale <> 3)
    ALTER TABLE modulus_security_audit ALTER COLUMN OccurredAt datetimeoffset(3) NOT NULL;
GO

COMMIT TRANSACTION;
GO
