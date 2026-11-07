---
sidebar_position: 9
title: Platform add-ons
description: Media, localization, notifications, secrets from OpenBao and tenant data lifecycle, each an opt-in package.
---

# Platform add-ons

These packages are opt-in. Each adds one capability to an existing host and keeps its own storage, so an app pays only for what it uses.

## Media

`Modulus.Media` handles uploaded media: validation, storage through `IFileStorage`, and an HTTP endpoint. Its metadata store is EF Core.

```csharp
services.AddModulusMedia();
services.AddModulusMediaStore<AppDbContext>();   // Modulus.Media.EntityFrameworkCore
...
app.MapModulusMedia();                            // /api/media by default
```

## Localization

`Modulus.Localization.EntityFrameworkCore` replaces the in-memory translation store with a database-backed one. The application's DbContext derives from `ModulusLocalizationDbContext`.

```csharp
services.AddModulusEfLocalizationStore<AppDbContext>();
```

Culture and translation settings bind to the `Localization` section.

## Notifications

Notifications are stored and delivered by the notifications module. The EF Core store is `Modulus.Notifications.EntityFrameworkCore`.

```csharp
services.AddModulusNotifications();
services.AddModulusNotificationsStore<AppDbContext>();
```

## Secrets from OpenBao

`Modulus.Secrets.OpenBao` reads secrets from an OpenBao server, so they never live in `appsettings.json`. Use it with the secrets guard, which refuses to start when a sensitive value comes from a committed file.

```csharp
services.AddOpenBaoSecretStore(builder.Configuration);   // read secrets
services.AddOpenBaoSecretWriter(builder.Configuration);  // write them (provisioning host only)
```

The generated provisioning host is the only one that holds a write identity. Authentication to OpenBao uses a Kubernetes service account or AppRole, set in configuration.

From the CLI:

```bash
modulus add-openbao
```

## Tenant data lifecycle

`Modulus.Tenancy.Lifecycle` handles a company's data at the end of its life: encrypted exports, legal holds, retention, and deletion certificates. Modules declare the datasets they own and implement their own handlers.

```csharp
services.AddTenantLifecycleStore<TStore>();          // or AddEfTenantLifecycleStore<TContext>()
...
app.MapModulusTenantLifecycle("/tenant-data");       // export endpoints
```

Retention can run on the platform scheduler (`TenantLifecycle:RetentionDriver`: `Scheduler`, with Quartz for a durable clustered schedule).

From the CLI:

```bash
modulus add-tenant-lifecycle                         # needs a multi-tenant app
```

Keys for exports and certificates are secrets: `TenantLifecycle:ExportEncryptionKey` and `TenantLifecycle:CertificateSigningKey`.
