# Value Object

An immutable object defined by its values, with no identity (e.g. money,
date ranges, locations).

In this sample value objects are represented pragmatically as enums and
primitives (`AttendeeStatus`, `GroupMemberRole`, `ProposalStatus`,
`RegistrationStatus`, fee + currency pairs). A future refactoring could
promote concepts like `MeetingTerm` or `MoneyValue` to full value objects —
the reference project (kgrzybek) shows that shape.
