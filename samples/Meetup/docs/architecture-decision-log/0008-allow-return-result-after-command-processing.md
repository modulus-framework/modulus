# 0008. Allow commands to return results

Date: 2026-09-15

Status: accepted

## Context

Strict CQRS forbids return values from commands, but REST clients need the id
of a newly created resource (and similar small results) immediately.

## Decision

Commands may return results (`ICommand<Guid>`, `ICommand<string>`, …).
`ProposeMeetingGroupCommand → Guid`, `JoinMeetingCommand → status string`,
`AuthenticateCommand → AuthenticateResult`.

## Consequences

- Endpoints can answer `201 Created` with a location and no extra round-trip.
- The command/query line stays blurry for cases like authentication (a command
  with a side effect that returns data) — accepted deliberately.
