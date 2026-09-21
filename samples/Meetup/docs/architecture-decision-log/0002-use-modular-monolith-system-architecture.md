# 0002. Use modular-monolith system architecture

Date: 2026-09-15

Status: accepted

## Context

The Meeting Groups domain has five natural bounded contexts
(UserAccess, Registrations, Administration, Payments, Meetings) that must ship
and run as one deployable today but stay separable tomorrow.

## Decision

Build a modular monolith: one process (`Meetup.Web`), autonomous modules with
explicit registration order (`AddModulus(…AddModule<…>…)`), no direct calls
between modules — integration only via events.

## Consequences

- Single deployment, no distributed-systems overhead (no network partitions,
  no sagas for local flows).
- Module boundaries must be defended (no cross-module references except
  integration-event contracts); extraction to microservices stays possible but
  is not free (at-least-once delivery, eventual consistency).
