# modulus add-tenant-lifecycle

Adds the tenant data lifecycle to a multi-tenant API app: exports of a company's data, offboarding, legal holds and retention.

```bash
modulus add-tenant-lifecycle
```

The command needs an app generated with multi-tenancy (`modulus app --multi-tenancy --kind api --auth openiddict`). An app without it is refused, because the lifecycle acts on companies.

## What it adds

- **A store module**, `src/Modules/{App}.Modules.TenantLifecycle/{App}.Modules.TenantLifecycle.Infrastructure`: the host database context that keeps lifecycle operations, legal holds and pending deletions. It belongs to the host, not to any company.
- **Host wiring**: the module registration, file storage (only when the host has none), `AddTenantLifecycle`, the export endpoints (`MapModulusTenantLifecycle`), and the `tenant-data:export` grant for the Admin role.
- **Settings**: the `TenantLifecycle` connection string.
- **A test class** that checks the export endpoints are closed to anonymous and to callers without the permission.

Running it again changes nothing.

## Before exports work

Two secrets must be set in the environment or user secrets, never in `appsettings.json`:

- `TenantLifecycle:ExportEncryptionKey`: 32 random bytes, base64.
- `TenantLifecycle:CertificateSigningKey`: at least 32 random bytes, base64.

Until they are set, export requests are refused with 503.

## What it does not do yet

The lifecycle has no tenant data to act on until modules declare their datasets with `ITenantDataHandler` and register them with `AddHandler`. A generated app has none, so an export holds only its manifest.
