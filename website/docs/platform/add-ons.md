---
sidebar_position: 9
title: Platform add-ons
description: Media, localization, notifications, secrets from OpenBao or Azure Key Vault and tenant data lifecycle, each an opt-in package.
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

## Secrets from Azure Key Vault

`Modulus.Secrets.AzureKeyVault` is the Azure alternative. It reads secrets through the Azure SDK, authenticated with `DefaultAzureCredential` (a managed identity or workload identity in Azure, your developer login locally), and serves them through the same cached `ISecretStore` as OpenBao.

```csharp
services.AddAzureKeyVaultSecretStore(builder.Configuration);   // read secrets
```

```json
"Secrets": {
  "AzureKeyVault": {
    "VaultUri": "https://contoso-prod.vault.azure.net/",
    "PathPrefix": "tenant-db"
  }
}
```

- A secret reference such as `tenant-db/3f2c/primary` becomes the Key Vault name `tenant-db--3f2c--primary` (Key Vault names allow only letters, digits and hyphens, up to 127 characters).
- A Key Vault secret holds one string. A JSON object is read as the secret's fields; any other string is read as the single field `value`.
- It is read-only. Key Vault has no atomic check-and-set on integer versions, so there is no `ISecretWriter`; provision secrets with Azure tooling.
- Key Vault versions are GUIDs, so the integer version the framework compares for rotation comes from the secret's last-updated time. Two writes in the same second share a version number.
- Covered by unit tests only; it has not been run against a live vault.

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
