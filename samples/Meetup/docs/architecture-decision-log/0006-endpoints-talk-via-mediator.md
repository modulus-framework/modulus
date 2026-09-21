# 0006. REPR endpoints talk to modules only via IMediator

Date: 2026-09-15

Status: accepted

## Context

The API must invoke module use cases without depending on their internals, and
adding an endpoint should not require touching shared abstractions.

## Decision

Presentation uses the REPR pattern (`Endpoint<TRequest, TResponse>` from
`Modulus.AspNetCore`, one file per endpoint, request DTO co-located) and calls
the application layer exclusively through `IMediator.SendAsync` /
`QueryAsync`. The host calls `AddMediator()` once; each module contributes its
handlers via `AddMediatorHandlers`.

## Consequences

- Endpoints stay thin adapters (HTTP ↔ command/query); handlers stay
  HTTP-agnostic and testable without a web host.
- The mediator adds one level of indirection when tracing a request to its
  handler (mitigated by the per-use-case folder layout).
