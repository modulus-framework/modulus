# 0004. Divide the system into 5 modules

Date: 2026-09-15

Status: accepted

## Context

Following the reference domain (kgrzybek/modular-monolith-with-ddd), the
system needs boundaries that match business capabilities, each with its own
data and lifecycle.

## Decision

Five business modules, each with its own `DbContext`, connection string,
migrations, and composition root (`{Module}Module`):

- **UserAccess** — authentication and user management.
- **Registrations** — sign-up and confirmation flow.
- **Administration** — meeting-group proposals.
- **Payments** — subscriptions and meeting-fee payments.
- **Meetings** — meeting groups, meetings, attendees, comments.

Shared, capability-neutral code lives in `Meetup.Shared.*`, never in another
business module.

## Consequences

- Per-module databases (five SQLite files in dev; five schemas/databases in
  production) — no shared tables, no cross-module joins.
- New capabilities should start as new modules, not as code inside an existing
  one.
