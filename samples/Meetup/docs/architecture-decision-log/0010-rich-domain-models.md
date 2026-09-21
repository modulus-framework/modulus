# 0010. Rich domain models, invariants guarded by exceptions

Date: 2026-09-15

Status: accepted

## Context

Business rules (paid-group requirement, organizer-only creation,
attendee-limit/waitlist, proposal lifecycle) must live in exactly one place
and be impossible to bypass.

## Decision

Aggregates expose behavior, not setters (`Meeting.Create`,
`MeetingAttendee.Join`, `MeetingGroupProposal.Accept/Reject`,
`UserRegistration.Confirm`, `Subscription.Renew`). Invariant violations throw
(`InvalidOperationException`, `ArgumentException`); the framework exception
handler maps them to HTTP responses. Tactical DDD patterns used: aggregates,
entities, enums-as-state, domain factories, repository contracts.

## Consequences

- Rules are enforced no matter which use case triggers them; handlers become
  thin orchestrators (load → act → commit → publish).
- Callers must treat domain exceptions as control flow for business failures;
  unexpected exceptions still surface as 500s with correlation ids.
