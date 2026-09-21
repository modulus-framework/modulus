# Entity

An object defined by its identity rather than its attributes, living inside an
aggregate.

In this sample: `MeetingAttendee` and `MeetingComment` (inside the `Meeting`
aggregate), `MeetingGroupMember` (inside `MeetingGroup`). All derive from
`AggregateRoot<Guid>`/`Entity` base types in `Modulus.Core.Abstractions.Domain`
and are persisted through the owning module's `DbContext`.
