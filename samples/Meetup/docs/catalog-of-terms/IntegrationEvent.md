# Integration Event

A published fact about the domain, forming the contract between modules. The
only thing modules may know about each other.

In this sample, one record per file under
`Application/IntegrationEvents` (e.g.
`MeetingGroupProposalAcceptedIntegrationEvent`,
`UserRegisteredIntegrationEvent`, `SubscriptionPurchasedIntegrationEvent`),
published via `IModuleBus.PublishAsync` and consumed by
`Application/IntegrationEventHandlers` in other modules. See README §3.7 for
the full event map.
