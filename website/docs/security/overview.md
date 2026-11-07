---
sidebar_position: 1
title: Security Model
description: How Modulus treats companies as tenants, closes endpoints by default and audits access decisions.
---

# Security Model

Modulus applications are built on a security model with four rules. Each one is enforced in code and checked at startup.

1. **A company is a tenant.** `ICurrentTenant` identifies the company. Branches, locations, factories and departments are org units inside a company. A group of companies is a set of tenants used only for federated reads.
2. **Closed by default.** Every endpoint needs a signed-in user unless it is explicitly opened with a reason. The startup guard refuses to start when an endpoint is left unprotected.
3. **Permissions come from a grant store.** Permissions use the `area:resource:action` form. Grants can be scoped to the user's own records, assigned records, an org unit or a whole company, and can have a validity period.
4. **Every decision is recorded.** Security events go to a hash-chained audit log, one chain per company.

## Tenancy

```csharp
builder.Services.AddMultiTenancy(t => t
    .UseJwtClaimResolver()
    .UseHeaderResolver()
    .RequireMembership());        // a user may enter only companies they belong to
```

- With `RequireMembership`, a user without a `tid` claim must have an active membership in the requested company, or the request gets `403`. Membership is the default in generated apps.
- A `tid` claim pins a token to one company.
- A company can be `Trial`, `Active`, `Suspended` or `Closed`. Suspended companies are read-only (`423` for writes). Closed companies no longer resolve.
- The write guard refuses inserts or changes that carry another company's tenant id (`403`) outside the token server's host context.

### Database isolation

The query filter is always applied. Beneath it, each provider gets the strongest control it supports:

| Provider | Shared database | One database per tenant |
|----------|-----------------|-------------------------|
| PostgreSQL | Row-level security | `AddModuleDatabasePerTenant` |
| SQL Server | Row-level security | `AddModuleDatabasePerTenant` |
| MySQL | Raw SQL guard only | `AddMySQLPerTenantDatabase` (recommended) |
| SQLite | Development only | `AddSQLitePerTenantDatabase` |
| MongoDB | Scoped collections | `AddMongoDatabasePerTenant` |

Raw SQL and `IgnoreQueryFilters()` on tenant tables are refused inside a tenant, unless they run in an explicit `CrossTenantSql.Allow(reason)` scope, which is audited. The application must not connect as the database owner.

## Opening an endpoint

Opening an endpoint is a recorded decision:

```csharp
app.MapGet("/health/public", () => Results.Ok())
   .Loosen("Public status page", ticket: "OPS-112");
```

Controllers and pages use `[AllowAnonymous, Loosened("reason")]`. The startup guard checks every endpoint when the host starts. It refuses an unprotected endpoint, an anonymous one without a reason, or anonymous access to confidential data. Outside Development, an opened endpoint must also be listed in `security/loosening-allowlist.json`.

The analyzer rule `MOD0001` warns at build time about anonymous access without a reason. `MOD0002` warns when a classified value is passed to an ordinary logger.

## Permissions and sensitivity

Permissions are classified as `Normal`, `Sensitive` or `Critical`. A wildcard grant never confers a Critical permission, and Critical permissions cannot be delegated. `authorization:manage` and `authorization:grant-any` are Critical.

Sensitive routes can require a recent multi-factor sign-in (step-up, RFC 9470). Integration clients are exempt only when their token carries the machine marker.

## Audit

- Tenant rejections, membership changes, authorization decisions, sign-in refusals, token issue and revocation, and cross-tenant SQL opt-ins are recorded.
- Events are written to a hash chain per company. `VerifyChainAsync` checks it, and anchors are published to an external sink.
- A database-backed store is available: `modulus add-audit-store` creates it.

Recorded events never contain user names, passwords, tokens or SQL text.

## Command-line support

```bash
modulus app Shop --kind api --auth openiddict --multi-tenancy
modulus add-audit-store
```

`--multi-tenancy` turns on membership checks, a tenant store, isolation tests and the security probe suite, which checks every endpoint for `401`, `403` and cross-company access.
