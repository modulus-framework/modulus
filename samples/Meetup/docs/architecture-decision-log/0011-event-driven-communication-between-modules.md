# 0011. Event-driven communication between modules

Date: 2026-09-15

Status: accepted

## Context

Modules must react to each other's state changes (registration → user,
proposal acceptance → group, subscription → paid-through date) without direct
references, so boundaries stay honest and a future extraction stays possible.

## Decision

Integration happens only through integration-event records published on
`IModuleBus` and handled in the consumer's
`Application/IntegrationEventHandlers` (e.g.
`MeetingGroupProposalAcceptedIntegrationEvent` →
`CreateGroupOnProposalAcceptedHandler`). In-process delivery; durable
outbox/inbox transport is roadmap item 5.

## Consequences

- Producers know nothing about consumers; new reactions plug in without
  touching the publisher.
- Delivery is currently in-memory: a crash between commit and handling loses
  the reaction — acceptable for the sample, not for production.
