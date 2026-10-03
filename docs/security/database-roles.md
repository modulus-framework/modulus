# Database roles for tenant isolation

Database-enforced company isolation (security plan, phase 3) only holds when the application connects as a
role that the database actually restricts. Use **two** roles per database:

| Role | Used by | Owns the schema | Subject to row-level security |
|---|---|---|---|
| Migration role | `modulus migrate update`, the migrator job / init container, `*RowLevelSecurity.EnsureAsync` | yes | no (needs to create policies) |
| Application role | the running app (`ConnectionStrings:*`) | no | yes |

Never run the app with the migration role: PostgreSQL skips row-level security for the table owner unless the
policy is `FORCE`d (Modulus forces it) and always for superusers and `BYPASSRLS` roles; on SQL Server an owner
can drop the security policy.

## PostgreSQL

```sql
-- Run as an administrator once per database.
CREATE ROLE app_migrator LOGIN PASSWORD '...' NOSUPERUSER NOBYPASSRLS;
CREATE ROLE app_runtime  LOGIN PASSWORD '...' NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB;

GRANT CONNECT ON DATABASE app TO app_migrator, app_runtime;
GRANT USAGE, CREATE ON SCHEMA public TO app_migrator;
GRANT USAGE ON SCHEMA public TO app_runtime;

-- Tables created later by the migrator are usable by the app, without ownership.
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO app_runtime;
ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO app_runtime;
```

Policies: `migrationBuilder.EnableTenantRowLevelSecurity("cat_products")` in a migration, or
`await PostgreSqlRowLevelSecurity.EnsureAsync(context)` as the migrator after migrating. The app registers
`services.AddPostgreSqlRowLevelSecurity<CatalogDbContext>()` next to `AddPostgreSQLDatabase<CatalogDbContext>(...)`.

Check: `SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = 'app_runtime';` must show `f, f`.

## SQL Server

```sql
-- Server level
CREATE LOGIN app_migrator WITH PASSWORD = '...';
CREATE LOGIN app_runtime  WITH PASSWORD = '...';

-- In the application database
CREATE USER app_migrator FOR LOGIN app_migrator;
CREATE USER app_runtime  FOR LOGIN app_runtime;
ALTER ROLE db_ddladmin ADD MEMBER app_migrator;
GRANT ALTER ANY SECURITY POLICY, ALTER ANY SCHEMA, REFERENCES TO app_migrator;

ALTER ROLE db_datareader ADD MEMBER app_runtime;
ALTER ROLE db_datawriter ADD MEMBER app_runtime;
-- app_runtime gets no db_owner, no db_ddladmin, no ALTER ANY SECURITY POLICY, no CONTROL on schema [modulus].
```

Policies: `migrationBuilder.EnableTenantRowLevelSecurity("cat_Products")` in a migration, or
`await SqlServerRowLevelSecurity.EnsureAsync(context)` as the migrator. The app registers
`services.AddSqlServerRowLevelSecurity<CatalogDbContext>()`.

## MySQL

MySQL has no row-level security. Prefer one database per tenant (`AddMySQLPerTenantDatabase`), each reachable
only by the app account:

```sql
CREATE USER 'app_migrator'@'%' IDENTIFIED BY '...';
CREATE USER 'app_runtime'@'%'  IDENTIFIED BY '...';
-- Per tenant database (t_<tenant id without dashes>) and the host database:
GRANT ALL PRIVILEGES ON `t\_%`.* TO 'app_migrator'@'%';
GRANT SELECT, INSERT, UPDATE, DELETE ON `t\_%`.* TO 'app_runtime'@'%';
```

With a shared MySQL database the EF filter, the write guard and the raw-SQL guard are the only controls; the
startup check (`Security:DataIsolation:Tier: Shared`) logs that residual risk.

## SQLite

Development only for shared files. For tenant data use one file per tenant (`AddSQLitePerTenantDatabase`); the
startup check refuses a shared SQLite file outside Development when `Security:DataIsolation:Tier` is set.
