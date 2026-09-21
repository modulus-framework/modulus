# 0005. Create one REST API host module

Date: 2026-09-15

Status: accepted

## Context

Clients need a single HTTP surface, while business logic must stay inside
modules.

## Decision

`Meetup.Web` is a thin host: middleware (correlation, idempotency, security
headers, exception handling), explicit module registration, endpoint mapping
(`MapModulusEndpoints`), health probes, OpenAPI/Scalar — plus the Razor + HTMX
companion UI (single host serves `/api/*` and pages; see README §3.10).
It owns no `DbContext` and no business logic.

## Consequences

- All HTTP concerns live in one place; modules stay transport-agnostic.
- The host cannot grow business logic without breaking this rule visibly.
