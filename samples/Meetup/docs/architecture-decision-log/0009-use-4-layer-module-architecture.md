# 0009. Use 4-layer Clean Architecture inside each module

Date: 2026-09-15

Status: accepted

## Context

Each module needs internal layering that keeps domain logic independent of
infrastructure while staying small enough for a sample (no separate Contracts
or IntegrationEvents projects).

## Decision

Four projects per module — Domain → Application → Infrastructure →
Presentation — with DTOs under `Application/Dtos` and integration events under
`Application/IntegrationEvents`. Dependencies point inward only; Infrastructure
implements the ports (`IUnitOfWork`, repositories) defined closer to the core.

## Consequences

- The domain model is isolated and unit-testable without EF Core or ASP.NET.
- Simpler than a 7-project split, at the cost of Application and
  Infrastructure sharing the same integration-event types (acceptable inside
  one deployable).
