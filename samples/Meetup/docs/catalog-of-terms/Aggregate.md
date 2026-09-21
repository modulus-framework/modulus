# Aggregate

A cluster of associated objects treated as a unit for data changes, with one
root that guards all invariants.

In this sample: `Meeting` (with its attendees/comments rules),
`MeetingGroup` (with membership), `MeetingGroupProposal`
(propose → accept/reject lifecycle), `UserRegistration`, `Subscription`,
`User`. Each aggregate is created through a static factory
(`Meeting.Create`, `MeetingGroupProposal.ProposeNew`, …) and has its own
repository.
