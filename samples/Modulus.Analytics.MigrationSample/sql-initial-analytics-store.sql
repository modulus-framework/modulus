CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104112124_InitialAnalyticsStore') THEN
    CREATE TABLE analytics_backfill_runs (
        "Id" uuid NOT NULL,
        "Dataset" character varying(128) NOT NULL,
        "SchemaVersion" integer NOT NULL,
        "TenantId" uuid NOT NULL,
        "Kind" integer NOT NULL,
        "Status" integer NOT NULL,
        "Cursor" character varying(512),
        "RowsQueued" bigint NOT NULL,
        "AttemptCount" integer NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "UpdatedAt" timestamp with time zone NOT NULL,
        "CompletedAt" timestamp with time zone,
        "NextAttemptAt" timestamp with time zone,
        "LockedBy" character varying(64),
        "LockedUntil" timestamp with time zone,
        "LastError" character varying(2048),
        "RequestedBy" character varying(128),
        CONSTRAINT "PK_analytics_backfill_runs" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104112124_InitialAnalyticsStore') THEN
    CREATE TABLE analytics_deliveries (
        "Id" uuid NOT NULL,
        "TenantId" uuid NOT NULL,
        "EventId" uuid NOT NULL,
        "Dataset" character varying(128) NOT NULL,
        "Revision" bigint NOT NULL,
        "SchemaVersion" integer NOT NULL,
        "OccurredAt" timestamp with time zone NOT NULL,
        "Payload" text NOT NULL,
        "CreatedAt" timestamp with time zone NOT NULL,
        "DeliveredAt" timestamp with time zone,
        "DeadLetteredAt" timestamp with time zone,
        "AttemptCount" integer NOT NULL,
        "NextAttemptAt" timestamp with time zone,
        "LockedBy" character varying(64),
        "LockedUntil" timestamp with time zone,
        "LastAttemptAt" timestamp with time zone,
        "LastError" character varying(2048),
        CONSTRAINT "PK_analytics_deliveries" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104112124_InitialAnalyticsStore') THEN
    CREATE UNIQUE INDEX "IX_analytics_backfill_runs_Dataset_SchemaVersion_TenantId_Kind" ON analytics_backfill_runs ("Dataset", "SchemaVersion", "TenantId", "Kind");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104112124_InitialAnalyticsStore') THEN
    CREATE INDEX "IX_analytics_backfill_runs_Status_NextAttemptAt" ON analytics_backfill_runs ("Status", "NextAttemptAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104112124_InitialAnalyticsStore') THEN
    CREATE INDEX "IX_analytics_deliveries_DeliveredAt_DeadLetteredAt_NextAttempt~" ON analytics_deliveries ("DeliveredAt", "DeadLetteredAt", "NextAttemptAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104112124_InitialAnalyticsStore') THEN
    CREATE INDEX "IX_analytics_deliveries_TenantId_CreatedAt" ON analytics_deliveries ("TenantId", "CreatedAt");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104112124_InitialAnalyticsStore') THEN
    CREATE UNIQUE INDEX "IX_analytics_deliveries_TenantId_Dataset_EventId_Revision" ON analytics_deliveries ("TenantId", "Dataset", "EventId", "Revision");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261104112124_InitialAnalyticsStore') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261104112124_InitialAnalyticsStore', '10.0.13');
    END IF;
END $EF$;
COMMIT;

