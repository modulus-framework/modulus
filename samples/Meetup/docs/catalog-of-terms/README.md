# Catalog of Terms

Ubiquitous-language glossary for this sample. Each term has its own file with
a short definition and a pointer to where it appears in the codebase. Terms are
borrowed from Eric Evans' DDD vocabulary and the patterns used across the
modules.

- [Aggregate](Aggregate.md) — consistency boundary (`Meeting`, `MeetingGroup`)
- [Entity](Entity.md) — identity-bearing object (`MeetingAttendee`, `User`)
- [Value Object](ValueObject.md) — expectation for future refactoring (enums-as-state today)
- [Command](Command.md) — write-side intent (`CreateMeetingCommand`)
- [Query](Query.md) — read-side intent (`GetProposalsQuery`)
- [Domain Event](DomainEvent.md) — in-module fact (outbox/dispatched domain events)
- [Integration Event](IntegrationEvent.md) — cross-module fact
  (`MeetingGroupProposalAcceptedIntegrationEvent`)
- [Event Storming](EventStorming.md) — process-first discovery this domain was modelled with
