# AI connector wire contract v1 (platform-neutral)

This document describes the HTTP contract that an application exposes so an external AI platform can use it. It is
written so that **any stack** (not only Modulus) can implement it. `Modulus.AI.Connector` is the reference
implementation; the shapes below come from `src/ai/Modulus.AI.Connector/Contract/WireContract.cs`, and where the two
differ the source wins.

## Principles

- **The app holds no AI logic.** Chat, planning, LLMs, embeddings, vector storage and retrieval belong to the platform.
  The app exposes data and read-only capabilities, nothing else.
- **Read-only.** A capability never changes data. Commands are not exposed.
- **Every answer is authorized as the user.** The app runs each call as the user the platform asserts, inside that
  user's company (tenant), with the app's own permission and field rules. The platform never decides access.
- **No caching of access decisions in the app; short caching in the platform** (scope TTL is at most 5 minutes) with
  push revocation.
- **Fail closed.** Any authentication problem is `401` with code `DENIED`; the reason goes to the app's audit trail,
  never to the caller.

## Transport and encoding

- JSON, `camelCase`, enums as their names, nulls written (a null is an answer), unknown response members ignored (a
  later minor version of the same major may add some).
- Capability **arguments** are strict: an unknown member is refused (`INVALID_REQUEST`).
- Base path is configurable (Modulus default `/_ai/connector`). The contract is versioned by **major** (`"1"`).
- Calls carrying a browser `Origin` header are refused.

## Authentication

| Call kind | Header | Meaning |
|---|---|---|
| Platform itself (`manifest`, `health`, `extract`, `changes`) | `Authorization: ApiKey <key>` | The app stores only SHA-256 hashes of at most two keys (rotation without downtime). |
| On behalf of a user (everything else) | `Authorization: ApiKey <key>` **and** `AiPlatform-Envelope: <jwt>` | The envelope is a short-lived JWT signed by the platform. |

Envelope rules (all must hold, else `DENIED`):

- Signed with an asymmetric algorithm (RSA, RSA-PSS or ECDSA) against the platform's published JWKS; issuer matches.
- `aud` contains the `app_instance_id` claim; the instance must be mapped to that `tenant_id` in the app.
- Has `jti` and `iat`; `exp - iat` is capped (default 5 min; the platform issues about 60 s); clock skew about 30 s.
- `jti` was not seen before (replay protection; per process in the reference implementation).
- Claims read: `sub` (the user, configurable), `email`, `tenant_id`, `app_instance_id`, `correlation_id`.
- The user resolves to an active account in the app; when the instance maps to a company, the company is active and
  the user is a member.

## Endpoints

| Method and path | Auth | Purpose |
|---|---|---|
| `GET /manifest` | key | What the app offers (below). |
| `GET /health` | key | `{ "status": "ok", "contractVersion": "1" }` |
| `POST /capabilities/{name}:execute` | key + envelope | Run a capability. Body `{ "args": { ... } }`. Answer `CapabilityResult`. |
| `POST /resources:get` | key + envelope | Body `{ "resourceType", "resourceId" }`. Answer `AppResource` (only fields the user may see). |
| `POST /authz/scope` | key + envelope | What the user may do: `AccessScope`. |
| `POST /authz/resources:check` | key + envelope | Body `{ "resources": [ResourceReference] }` (batch capped, default 100). Answer decisions in request order. |
| `POST /authz/fields:check` | key + envelope | Body `{ "resource", "fields": [..] }`. Answer `{ "allowedFields": [..] }`. |
| `GET /extract?appInstanceId&resourceType&cursor&limit` | key (service identity) | Paged full extraction for indexing. |
| `GET /changes?appInstanceId&since&limit` | key (service identity) | Incremental changes after a cursor. |

`extract` and `changes` run as a per-instance **indexing identity** (a role in the instance's company), not as a user.
An unconfigured `appInstanceId` is `403`.

## Shapes

```text
ConnectorManifest   { contractVersion, appType, appName, fingerprint, capabilities[], resourceTypes[] }
ManifestCapability  { name, description, readOnly:true, inputSchema(JSON Schema), requiredPermissions[],
                      resourceType?, outputFields[] }
ManifestResourceType{ type, description, deepLink?, titleField?, indexed, fields[] }
ManifestField       { name, type, classification }        // secret fields are never listed
ResourceReference   { resourceType, resourceId, namespace? }
AppResource         { reference, fields{name: json}, deepLink }
CapabilityResult    { resources[AppResource], truncated }
AccessScope         { appInstanceId, roles[], permissions[], dataScopes{k: string[]},
                      fieldPolicies{ name: "Allow"|"Deny" }, ttlSeconds, revocationKey }
IndexedResource     { reference, fields{}, deepLink, access:{ requiredPermissions[], dataScopes{} } }
ExtractPage         { resources[IndexedResource], nextCursor? }   // null cursor = complete
ResourceChange      { kind:"Upsert"|"Tombstone", reference, resource?, occurredAt }
ChangesPage         { changes[], cursor, hasMore }                 // pass cursor back as `since`
```

- `fingerprint` changes exactly when what the platform sees changes, so a deploy that changes nothing visible needs no
  new approval.
- Field `classification` is one of the platform data classes. A field with no declared class is treated as
  **Confidential**, never public.
- Deep links are relative unless the app has a public base URL.

## Errors

`{ "code": "...", "message": "..." }`

| Code | Meaning | Platform behavior |
|---|---|---|
| `DENIED` | Not allowed, or any authentication failure (`401`/`403`) | Final, never retried |
| `NOT_FOUND` | No such capability, resource type or record, or the user may not know it exists | Final |
| `UNAVAILABLE` | Timeout or failing dependency (default call timeout 10 s) | Retried |
| `RATE_LIMITED` | Too many calls | Retried with back-off |
| `INVALID_REQUEST` | Arguments do not match the input schema | Treated as a deny |

## Indexing and RAG

The platform builds its own embeddings and vector index from `/extract` and `/changes`. The app guarantees:

1. **Complete, resumable extraction** by opaque cursor.
2. **Deletes and loss of visibility arrive as `Tombstone`** changes (deleted, soft-deleted, or no longer visible to the
   indexing identity), so the index does not keep removed or re-scoped data.
3. **Change journal written in the same transaction as the data**, so a change cannot be lost between commit and
   journal. Changes younger than a settle delay (default 5 s) are withheld so an earlier, still-committing sequence
   number is not skipped.
4. **Access metadata per record** (`access.requiredPermissions`, `access.dataScopes`) for platform-side pre-filtering.
5. **Optional change hint**: a signed `{ appInstanceId, eventId, occurredAt }` POST (Standard Webhooks signature) so the
   platform polls `/changes` sooner. A hint never carries data.

**Retrieval rule for platform implementers:** pre-filtering by `access` is an optimization only. Before a retrieved
record is used in an answer, re-authorize it with `/authz/resources:check` (or fetch it with `/resources:get`), and mask
fields with `/authz/fields:check`. The index may be stale or over-inclusive; the app is the authority.

## Revocation

When access changes (grants, roles, memberships, accounts), the app POSTs to the platform's `/revocations/scope`:

```text
RevocationSignal { revocationKey, appInstanceId, reason, occurredAt }
```

The same payload is used on every retry. The platform must drop cached scopes for that `revocationKey`.

## Conformance

A conformant app passes these categories: health, authentication, tenant isolation, manifest, deny paths, query
injection, field security, batch authorization, extraction, revocation, no adapter-side caching, typed errors. In
Modulus run them with `AiConnectorConformance.RunAsync` (package `Modulus.AI.Connector.Testing`). Other stacks can
reimplement the same checks against this document.

## Not in this contract

- Writes or commands (read-only by design).
- MCP, or any agent protocol. These are adapters over this contract, not part of it. A separate adapter that exposes
  the manifest's capabilities as MCP tools and its resource types as MCP resources, for agents that are not the
  platform, is not built and `docs/ADVANCED_FEATURES_PLAN.md` currently lists it as not planned. If it is ever added
  it must reuse the same per-user authorization and audit path.
