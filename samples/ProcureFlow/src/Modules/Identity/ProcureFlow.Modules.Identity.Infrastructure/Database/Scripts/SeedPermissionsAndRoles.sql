-- ==========================================
-- Permissions and RolePermissions Seed Script
-- V2 Resource-Based Permissions
-- Last Updated: 2026-06-10
-- ==========================================

BEGIN;

-- 0a. Revoke kitchen:read from Customer role (kitchen photos are admin-only)
DELETE FROM "identity"."role_permissions"
WHERE "permission_id" = 'kitchen:read'
  AND "role_id" = (SELECT id FROM "identity"."roles" WHERE name = 'Customer');

-- 0. Remove stale V1 permissions that no longer exist in the codebase
DELETE FROM "identity"."role_permissions"
WHERE "permission_id" NOT IN (
    'orders:read', 'orders:write', 'orders:admin',
    'kitchen:read', 'kitchen:write', 'kitchen:admin', 'kitchen:verify',
    'menu:read', 'menu:write', 'menu:admin',
    'basket:read', 'basket:write', 'basket:admin',
    'payments:read', 'payments:write', 'payments:admin',
    'delivery:read', 'delivery:write', 'delivery:admin',
    'reviews:read', 'reviews:write', 'reviews:moderate',
    'notifications:read', 'notifications:write', 'notifications:admin',
    'media:upload', 'media:manage', 'media:admin',
    'promotions:redeem', 'promotions:admin',
    'cms:read', 'cms:write',
    'analytics:view', 'analytics:export',
    'platform:dashboard', 'platform:audit', 'platform:settings', 'platform:compliance',
    'moderation:users', 'moderation:content', 'moderation:disputes',
    'support:read', 'support:write', 'support:admin',
    'inspector:read', 'inspector:write'
);

DELETE FROM "identity"."permissions"
WHERE "code" NOT IN (
    'orders:read', 'orders:write', 'orders:admin',
    'kitchen:read', 'kitchen:write', 'kitchen:admin', 'kitchen:verify',
    'menu:read', 'menu:write', 'menu:admin',
    'basket:read', 'basket:write', 'basket:admin',
    'payments:read', 'payments:write', 'payments:admin',
    'delivery:read', 'delivery:write', 'delivery:admin',
    'reviews:read', 'reviews:write', 'reviews:moderate',
    'notifications:read', 'notifications:write', 'notifications:admin',
    'media:upload', 'media:manage', 'media:admin',
    'promotions:redeem', 'promotions:admin',
    'cms:read', 'cms:write',
    'analytics:view', 'analytics:export',
    'platform:dashboard', 'platform:audit', 'platform:settings', 'platform:compliance',
    'moderation:users', 'moderation:content', 'moderation:disputes',
    'support:read', 'support:write', 'support:admin',
    'inspector:read', 'inspector:write'
);

-- 1. Seed new V2 permissions (additive only - don't delete old ones)
INSERT INTO "identity"."permissions" ("id", "code", "name", "description", "category", "is_active")
SELECT gen_random_uuid(), code, name, description, category, true
FROM (VALUES
    -- Orders
    ('orders:read', 'Orders Read', 'View orders', 'Orders', true),
    ('orders:write', 'Orders Write', 'Create and modify orders', 'Orders', true),
    ('orders:admin', 'Orders Admin', 'Manage all orders', 'Orders', true),
    -- Kitchen
    ('kitchen:read', 'Kitchen Read', 'Browse kitchen profiles', 'Kitchen', true),
    ('kitchen:write', 'Kitchen Write', 'Manage own kitchen', 'Kitchen', true),
    ('kitchen:admin', 'Kitchen Admin', 'Manage any kitchen', 'Kitchen', true),
    ('kitchen:verify', 'Kitchen Verify', 'Approve certifications', 'Kitchen', true),
    -- Menu
    ('menu:read', 'Menu Read', 'Browse menu items', 'Menu', true),
    ('menu:write', 'Menu Write', 'Manage own menu', 'Menu', true),
    ('menu:admin', 'Menu Admin', 'Manage any menu', 'Menu', true),
    -- Basket
    ('basket:read', 'Basket Read', 'View own basket', 'Basket', true),
    ('basket:write', 'Basket Write', 'Manage own basket', 'Basket', true),
    ('basket:admin', 'Basket Admin', 'View any basket', 'Basket', true),
    -- Payments
    ('payments:read', 'Payments Read', 'View payment history', 'Payments', true),
    ('payments:write', 'Payments Write', 'Make payments, manage payout method', 'Payments', true),
    ('payments:admin', 'Payments Admin', 'Process refunds, manage payouts', 'Payments', true),
    -- Delivery
    ('delivery:read', 'Delivery Read', 'View delivery status', 'Delivery', true),
    ('delivery:write', 'Delivery Write', 'Update own delivery', 'Delivery', true),
    ('delivery:admin', 'Delivery Admin', 'Manage all deliveries', 'Delivery', true),
    -- Reviews
    ('reviews:read', 'Reviews Read', 'View all reviews', 'Reviews', true),
    ('reviews:write', 'Reviews Write', 'Post and respond to reviews', 'Reviews', true),
    ('reviews:moderate', 'Reviews Moderate', 'Remove inappropriate reviews', 'Reviews', true),
    -- Notifications
    ('notifications:read', 'Notifications Read', 'View own notifications', 'Notifications', true),
    ('notifications:write', 'Notifications Write', 'Send notifications', 'Notifications', true),
    ('notifications:admin', 'Notifications Admin', 'Manage templates, bulk send', 'Notifications', true),
    -- Media
    ('media:upload', 'Media Upload', 'Upload images', 'Media', true),
    ('media:manage', 'Media Manage', 'Manage own uploads', 'Media', true),
    ('media:admin', 'Media Admin', 'Manage all media', 'Media', true),
    -- Promotions
    ('promotions:redeem', 'Promotions Redeem', 'Redeem codes, earn loyalty', 'Promotions', true),
    ('promotions:admin', 'Promotions Admin', 'Create and manage promotions', 'Promotions', true),
    -- Content
    ('cms:read', 'CMS Read', 'View CMS content (announcements, FAQs, pages, testimonials, blog, careers, press)', 'CMS', true),
    ('cms:write', 'CMS Write', 'Manage platform CMS content', 'CMS', true),
    -- Analytics
    ('analytics:view', 'Analytics View', 'View dashboards', 'Analytics', true),
    ('analytics:export', 'Analytics Export', 'Export raw data', 'Analytics', true),
    -- Platform Admin
    ('platform:dashboard', 'Platform Dashboard', 'Admin panel access', 'Platform', true),
    ('platform:audit', 'Platform Audit', 'View audit logs', 'Platform', true),
    ('platform:settings', 'Platform Settings', 'Platform configuration', 'Platform', true),
    ('platform:compliance', 'Platform Compliance', 'Compliance reports, KYC queue', 'Platform', true),
    -- Moderation
    ('moderation:users', 'Moderation Users', 'Suspend or ban users', 'Moderation', true),
    ('moderation:content', 'Moderation Content', 'Moderate listings and media', 'Moderation', true),
    ('moderation:disputes', 'Moderation Disputes', 'Resolve order disputes', 'Moderation', true),
    -- Support
    ('support:read', 'Support Read', 'View own tickets', 'Support', true),
    ('support:write', 'Support Write', 'Create and respond to tickets', 'Support', true),
    ('support:admin', 'Support Admin', 'Manage all tickets', 'Support', true),
    -- Inspector
    ('inspector:read', 'Inspector Read', 'View assigned inspections', 'Inspector', true),
    ('inspector:write', 'Inspector Write', 'Submit inspection reports', 'Inspector', true)
) AS v(code, name, description, category, is_active)
WHERE NOT EXISTS (
    SELECT 1 FROM "identity"."permissions" p WHERE p.code = v.code
);

-- 2. Ensure roles exist with new names (upsert)
INSERT INTO "identity"."roles" (id, name, description, is_system, created_at_utc, version)
VALUES
    (gen_random_uuid(), 'Admin', 'Platform administrator with full system access', true, NOW() AT TIME ZONE 'UTC', 1),
    (gen_random_uuid(), 'Cook', 'Restaurant staff who prepares food and manages menu items', true, NOW() AT TIME ZONE 'UTC', 1),
    (gen_random_uuid(), 'DeliveryDriver', 'Delivery driver for order deliveries', true, NOW() AT TIME ZONE 'UTC', 1),
    (gen_random_uuid(), 'Customer', 'Customer who places orders', true, NOW() AT TIME ZONE 'UTC', 1),
    (gen_random_uuid(), 'Analyst', 'System analyst with view-only analytics access', true, NOW() AT TIME ZONE 'UTC', 1),
    (gen_random_uuid(), 'Support', 'Customer support agent with limited management access', true, NOW() AT TIME ZONE 'UTC', 1),
    (gen_random_uuid(), 'Inspector', 'Food safety inspector', true, NOW() AT TIME ZONE 'UTC', 1),
    (gen_random_uuid(), 'Manager', 'Operational manager with broad access', true, NOW() AT TIME ZONE 'UTC', 1),
    (gen_random_uuid(), 'Moderator', 'Content and user moderator', true, NOW() AT TIME ZONE 'UTC', 1)
ON CONFLICT (name) DO NOTHING;

-- 3. Seed V2 role-permission mappings
-- Admin gets all 46 V2 permissions
INSERT INTO "identity"."role_permissions" (id, role_id, permission_id, granted_by_user_id, granted_at_utc, is_active)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    (SELECT id FROM "identity"."users" LIMIT 1),
    NOW() AT TIME ZONE 'UTC',
    true
FROM "identity"."roles" r
CROSS JOIN "identity"."permissions" p
WHERE r.name = 'Admin'
  AND p.code IN (
    'orders:read', 'orders:write', 'orders:admin',
    'kitchen:read', 'kitchen:write', 'kitchen:admin', 'kitchen:verify',
    'menu:read', 'menu:write', 'menu:admin',
    'basket:read', 'basket:write', 'basket:admin',
    'payments:read', 'payments:write', 'payments:admin',
    'delivery:read', 'delivery:write', 'delivery:admin',
    'reviews:read', 'reviews:write', 'reviews:moderate',
    'notifications:read', 'notifications:write', 'notifications:admin',
    'media:upload', 'media:manage', 'media:admin',
    'promotions:redeem', 'promotions:admin',
    'cms:read', 'cms:write',
    'analytics:view', 'analytics:export',
    'platform:dashboard', 'platform:audit', 'platform:settings', 'platform:compliance',
    'moderation:users', 'moderation:content', 'moderation:disputes',
    'support:read', 'support:write', 'support:admin',
    'inspector:read', 'inspector:write'
  )
  AND NOT EXISTS (
      SELECT 1 FROM "identity"."role_permissions" rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id AND rp.is_active = true
  );

-- Cook permissions
INSERT INTO "identity"."role_permissions" (id, role_id, permission_id, granted_by_user_id, granted_at_utc, is_active)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    (SELECT id FROM "identity"."users" LIMIT 1),
    NOW() AT TIME ZONE 'UTC',
    true
FROM "identity"."roles" r
CROSS JOIN "identity"."permissions" p
WHERE r.name = 'Cook'
  AND p.code IN (
    'orders:read',
    'kitchen:read', 'kitchen:write',
    'menu:read', 'menu:write',
    'payments:read', 'payments:write',
    'media:upload', 'media:manage',
    'notifications:read',
    'reviews:read', 'reviews:write',
    'analytics:view',
    'support:read', 'support:write',
    'cms:read'
  )
  AND NOT EXISTS (
      SELECT 1 FROM "identity"."role_permissions" rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id AND rp.is_active = true
  );

-- Customer permissions
INSERT INTO "identity"."role_permissions" (id, role_id, permission_id, granted_by_user_id, granted_at_utc, is_active)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    (SELECT id FROM "identity"."users" LIMIT 1),
    NOW() AT TIME ZONE 'UTC',
    true
FROM "identity"."roles" r
CROSS JOIN "identity"."permissions" p
WHERE r.name = 'Customer'
  AND p.code IN (
    'orders:read', 'orders:write',
    'basket:read', 'basket:write',
    'menu:read',
    'reviews:read', 'reviews:write',
    'promotions:redeem',
    'media:upload',
    'notifications:read',
    'support:read', 'support:write',
    'cms:read'
  )
  AND NOT EXISTS (
      SELECT 1 FROM "identity"."role_permissions" rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id AND rp.is_active = true
  );

-- DeliveryDriver permissions
INSERT INTO "identity"."role_permissions" (id, role_id, permission_id, granted_by_user_id, granted_at_utc, is_active)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    (SELECT id FROM "identity"."users" LIMIT 1),
    NOW() AT TIME ZONE 'UTC',
    true
FROM "identity"."roles" r
CROSS JOIN "identity"."permissions" p
WHERE r.name = 'DeliveryDriver'
  AND p.code IN (
    'orders:read',
    'delivery:read', 'delivery:write',
    'payments:read',
    'notifications:read',
    'support:read', 'support:write',
    'cms:read'
  )
  AND NOT EXISTS (
      SELECT 1 FROM "identity"."role_permissions" rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id AND rp.is_active = true
  );

-- Analyst permissions
INSERT INTO "identity"."role_permissions" (id, role_id, permission_id, granted_by_user_id, granted_at_utc, is_active)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    (SELECT id FROM "identity"."users" LIMIT 1),
    NOW() AT TIME ZONE 'UTC',
    true
FROM "identity"."roles" r
CROSS JOIN "identity"."permissions" p
WHERE r.name = 'Analyst'
  AND p.code IN (
    'orders:read',
    'payments:read', 'payments:write', 'payments:admin',
    'analytics:view', 'analytics:export',
    'platform:dashboard', 'platform:audit', 'platform:compliance',
    'cms:read'
  )
  AND NOT EXISTS (
      SELECT 1 FROM "identity"."role_permissions" rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id AND rp.is_active = true
  );

-- Support permissions
INSERT INTO "identity"."role_permissions" (id, role_id, permission_id, granted_by_user_id, granted_at_utc, is_active)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    (SELECT id FROM "identity"."users" LIMIT 1),
    NOW() AT TIME ZONE 'UTC',
    true
FROM "identity"."roles" r
CROSS JOIN "identity"."permissions" p
WHERE r.name = 'Support'
  AND p.code IN (
    'orders:read', 'orders:admin',
    'payments:read',
    'delivery:read', 'delivery:admin',
    'reviews:read', 'reviews:moderate',
    'notifications:read', 'notifications:write',
    'support:read', 'support:write', 'support:admin',
    'analytics:view',
    'platform:dashboard',
    'moderation:content', 'moderation:disputes',
    'cms:read'
  )
  AND NOT EXISTS (
      SELECT 1 FROM "identity"."role_permissions" rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id AND rp.is_active = true
  );

-- Inspector permissions
INSERT INTO "identity"."role_permissions" (id, role_id, permission_id, granted_by_user_id, granted_at_utc, is_active)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    (SELECT id FROM "identity"."users" LIMIT 1),
    NOW() AT TIME ZONE 'UTC',
    true
FROM "identity"."roles" r
CROSS JOIN "identity"."permissions" p
WHERE r.name = 'Inspector'
  AND p.code IN (
    'kitchen:read', 'kitchen:verify',
    'menu:read',
    'platform:compliance',
    'inspector:read', 'inspector:write',
    'cms:read'
  )
  AND NOT EXISTS (
      SELECT 1 FROM "identity"."role_permissions" rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id AND rp.is_active = true
  );

-- Manager permissions (operational oversight, no full platform settings)
INSERT INTO "identity"."role_permissions" (id, role_id, permission_id, granted_by_user_id, granted_at_utc, is_active)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    (SELECT id FROM "identity"."users" LIMIT 1),
    NOW() AT TIME ZONE 'UTC',
    true
FROM "identity"."roles" r
CROSS JOIN "identity"."permissions" p
WHERE r.name = 'Manager'
  AND p.code IN (
    'orders:read', 'orders:write', 'orders:admin',
    'kitchen:read', 'kitchen:write', 'kitchen:admin',
    'menu:read', 'menu:write', 'menu:admin',
    'basket:read', 'basket:admin',
    'payments:read', 'payments:write', 'payments:admin',
    'delivery:read', 'delivery:write', 'delivery:admin',
    'reviews:read', 'reviews:write', 'reviews:moderate',
    'notifications:read', 'notifications:write', 'notifications:admin',
    'media:upload', 'media:manage', 'media:admin',
    'promotions:redeem', 'promotions:admin',
    'cms:read', 'cms:write',
    'analytics:view', 'analytics:export',
    'platform:dashboard', 'platform:audit', 'platform:compliance',
    'moderation:users', 'moderation:content', 'moderation:disputes',
    'support:read', 'support:write', 'support:admin',
    'inspector:read', 'inspector:write'
  )
  AND NOT EXISTS (
      SELECT 1 FROM "identity"."role_permissions" rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id AND rp.is_active = true
  );

-- Moderator permissions (content and user moderation)
INSERT INTO "identity"."role_permissions" (id, role_id, permission_id, granted_by_user_id, granted_at_utc, is_active)
SELECT
    gen_random_uuid(),
    r.id,
    p.id,
    (SELECT id FROM "identity"."users" LIMIT 1),
    NOW() AT TIME ZONE 'UTC',
    true
FROM "identity"."roles" r
CROSS JOIN "identity"."permissions" p
WHERE r.name = 'Moderator'
  AND p.code IN (
    'orders:read',
    'kitchen:read',
    'menu:read', 'menu:admin',
    'reviews:read', 'reviews:moderate',
    'notifications:read', 'notifications:write',
    'media:manage', 'media:admin',
    'cms:read', 'cms:write',
    'analytics:view',
    'platform:dashboard', 'platform:audit',
    'moderation:users', 'moderation:content', 'moderation:disputes',
    'support:read', 'support:write', 'support:admin'
  )
  AND NOT EXISTS (
      SELECT 1 FROM "identity"."role_permissions" rp
      WHERE rp.role_id = r.id AND rp.permission_id = p.id AND rp.is_active = true
  );

COMMIT;