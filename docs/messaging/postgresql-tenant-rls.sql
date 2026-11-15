-- Row-level isolation of the queue table by company (FR-21). Run as the table owner, once per table.
-- The transport role's members see every row; any other session sees its company's rows.
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'modulus_transport') THEN
        CREATE ROLE "modulus_transport" NOLOGIN;
    END IF;
END $$;
ALTER TABLE "public"."messaging_queue" ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS modulus_queue_tenant ON "public"."messaging_queue";
CREATE POLICY modulus_queue_tenant ON "public"."messaging_queue"
    USING (tenant_id = current_setting('modulus.tenant', true)
 OR (tenant_id IS NULL AND current_setting('modulus.tenant', true) = 'host')
 OR pg_has_role(current_user, 'modulus_transport', 'MEMBER'))
    WITH CHECK (tenant_id = current_setting('modulus.tenant', true)
 OR (tenant_id IS NULL AND current_setting('modulus.tenant', true) = 'host')
 OR pg_has_role(current_user, 'modulus_transport', 'MEMBER'));
