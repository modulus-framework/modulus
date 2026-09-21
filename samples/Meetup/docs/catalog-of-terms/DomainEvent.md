# Domain Event

Something that happened inside one bounded context that domain experts care
about.

In this sample, aggregates raise domain events that `ModuleDbContext`
dispatches on `SaveChangesAsync` (via the framework's `DomainEventDispatcher`)
and that are fanned out to the transactional outbox when they implement
`IIntegrationEvent`. Handlers live next to the use cases that raise them.
