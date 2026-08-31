# ModulusSampleErp — Testing Guide

This guide walks the full end-to-end setup: infrastructure stack → API startup
(migrations + seeding) → login → exercising every module over HTTP.

## Prerequisites

- .NET SDK **10.0.109** or newer
- Docker (for the Postgres / Redis / RabbitMQ / MinIO stack)
- The framework packages must be packed to the local feed first (the sample's
  `NuGet.config` points at the repo-local `nupkg/` folder):

```bash
# from the repo root  E:\Personal\framework\modulus
dotnet pack modulus.slnx -c Release
```

## 1. Start the infrastructure

```bash
cd samples/ModulusSampleErp
docker compose up -d
docker compose ps   # wait until all services are healthy
```

| Service    | Port(s)        | Purpose                                    |
|------------|----------------|--------------------------------------------|
| postgres   | 5432           | Single shared database (per-module Postgres schemas) |
| redis      | 6379           | Cache + Data Protection key ring           |
| rabbitmq   | 5672 / 15672  | Event bus (console at http://localhost:15672, guest/guest) |
| minio      | 9000 / 9001   | S3-compatible storage for Media uploads (console at http://localhost:9001, minioadmin/minioadmin) |
| api        | 5016 → 8080   | The ModulusSample.Api container (builds the Dockerfile, migrates on boot) |

The whole app shares **one Postgres database** (`ModulusSample`). Each module's
`DbContext` owns its tables in its own schema (`identity`, `settings`,
`tenants`, `features`, `virtual_file_explorer`, `notifications`,
`media`) and keeps its own EF migrations + per-schema `__EFMigrationsHistory`
table — so modules stay independently deployable without separate databases.
On first boot Postgres creates the single DB and the API's `--migrate` (entrypoint,
`RUN_MIGRATIONS=true`) creates the schemas.

The older SQLite Media database (`media.db`) is gone — Media now uses the shared
Postgres DB too (schema `media`), with file blobs in MinIO.

## 2. Build & seed

```bash
dotnet build ModulusSampleErp.slnx
dotnet run --project src/API/ModulusSample.Api -- --seed
```

Flags
- `--migrate` — creates schemas only (`Migrate` when the context has EF migrations —
  all module contexts have them).
- `--seed`    — runs migrations + seeding (identity permissions/roles/users **and**
  the `SampleDataSeeder` sample records below).
- Plain `dotnet run` also applies migrations on startup in Development (with the
  EF-design-time check), so the `--` flags are only needed for a one-shot pass.

> Per-module connection strings live in `src/API/ModulusSample.Api/modules.<module>.json`
> under `ConnectionStrings` (Identity uses `modules.identity.json`). All of them
> target the single `ModulusSample` DB and match the `docker compose` credentials;
> `ConnectionStrings.Database`/`Cache` stay in `appsettings.json`. `X-Tenant-ID` is
> not required — seeded sample data uses `Guid.Empty` (no tenant scope), so every
> endpoint below returns rows.

## 3. Run the API

```bash
dotnet run --project src/API/ModulusSample.Api
```

The Kestrel port when running via `dotnet run` is chosen by `launchSettings.json`
(http://localhost:5016). Health probe: http://localhost:5016/health.
OpenAPI: http://localhost:5016/openapi/v1.json, Swagger UI: /swagger.

## 4. Seeded sample data (after `--seed`)

| Module               | Records                                                                 |
|----------------------|-------------------------------------------------------------------------|
| Identity             | permissions, roles (`Admin`/`User`), users `admin`/`Admin123!` & `user1`/`User123!` (now with BCrypt `PasswordHash` so the password grant works) |
| Settings             | `app.name`, `app.default-locale`, `company.support-email`, `notifications.email.enabled` |
| Tenants              | `Acme Corporation` (`acme`), `Globex Ltd` (`globex`)                      |
| Features              | `catalog.new-checkout` (on), `catalog.promo-banners` (on), `import.export` (off) |
| VirtualFileExplorer   | folders `Contracts`, `Invoices`, `Product-Photos`                          |
| Notifications          | 2 welcome notifications for `admin`                                       |
| Media (shared Postgres, schema `media`) | folders `Products`, `Marketing`, `Products/Product A` |

## 4. Postman collection

Import `ModulusSampleErp.postman_collection.json`.

1. Set the `baseUrl` variable to your API origin (e.g. `http://localhost:5016`).
2. Open **1. Identity (auth) → Login (password grant)** and hit **Send**. The
   test script stores the returned `access_token` into the collection variable
   `token`; every later request is authorised via the collection-level bearer token.
3. Walk folders **Settings**, **Tenants**, **Features**, **VirtualFileExplorer**,
   **Notifications**, and **Media**. Each `Create` request captures its generated id
   into a variable used by the following detail requests. Create requests also set
   `Idempotency-Key` on POST/PATCH retries — send the same key twice and watch the
   4th row replay (see below).

### MinIO / Media upload

Media stores files in bucket `modulussample-uploads` created by the
`minio-init` one-shot container. Uploads hit `POST /api/media/files`
(form-data `file`). Before uploading, ensure the file's folder exists
(`POST /api/media/folders`). Presigned GET URLs are exposed at
`GET /api/media/files/{id}/presigned-url`.

## 6. Idempotency (optional check)

`Idempotency-Key` header (config `Idempotency:HeaderName`) is honored on
POST/PATCH. Send any `Create` request twice with the same key:
- first POSTs executes → response buffers
- second POST replayed, body carries `Idempotency-Replayed: true`
- same key + different payload → `422`

## 7. Docker build of the API

```bash
docker build -f src/API/ModulusSample.Api/Dockerfile -t modulus-sample-api .
```

Or run the whole stack (uses the compose `api` service, migrations run in the
container's entrypoint):

```bash
docker compose up -d
docker compose ps   # wait until postgres/redis/rabbitmq/minio are healthy and api is up
# API: http://localhost:5016/health, Swagger UI: http://localhost:5016/swagger
```

## Troubleshooting

- **Transient connect errors** — Postgres/Redis/RabbitMQ may still be starting;
  wait for `docker compose ps` to show `healthy`, then re-run migrations.
- **"Access denied" / MinIO** — `minio-init` creates the bucket once. If the
  bucket is missing, `docker compose up minio-init` again.
- **Ports already in use** — override in `docker-compose.yml`; then update the
  matching `ConnectionStrings` in `modules.<module>.json` (or the compose `api`
  environment overrides).

## 8. Automated tests

- **Unit tests** — `src/Modules/Identity/ModulusSample.Modules.Identity.UnitTests`
  runs without external dependencies:

  ```bash
  dotnet test src/Modules/Identity/ModulusSample.Modules.Identity.UnitTests
  ```

- **Integration tests** — `tests/ModulusSampleErp.IntegrationTests` boots the
  full host via `Modulus.Testing` (`ModulusWebAppFactory`), which swaps every
  module `DbContext` to a per-factory in-memory SQLite database.

  **Known limitation:** the Identity module model is authored for PostgreSQL — it
  uses Npgsql-only default SQL like
  `HasDefaultValueSql("(NOW() AT TIME ZONE 'UTC')")` and `timestamp with time
  zone` columns. SQLite cannot build that schema, so the integration tests
  currently do **not** pass in this setup; they fail at host startup with
  `SQLite Error: near "AT": syntax error`. Running them requires the real
  Postgres stack from section 1. The unit tests above plus the manual flows in
  sections 2–6 cover the sample in a Postgres-less environment.