# 0007. Use CQRS for module requests

Date: 2026-09-15

Status: accepted

## Context

Reads (lists, details) and writes (state transitions with invariants) have
different needs: writes go through the rich domain model, reads project to
DTOs.

## Decision

Every use case is an explicit command or query object with a single handler
(`ICommandHandler<T, TResult>` / `IQueryHandler<T, R>`). Writes load
aggregates, call behavior methods, and commit via the module's `IUnitOfWork`;
reads project entities to DTOs without domain behavior.

## Consequences

- One handler does one thing; cross-cutting concerns (validation,
  transactions, logging) apply uniformly in the mediator pipeline.
- More files than a CRUD controller — mitigated by use-case folders and the
  one-type-per-file rule.
