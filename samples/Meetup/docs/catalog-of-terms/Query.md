# Query

An object describing a read that changes nothing, handled by a single query
handler projecting to DTOs.

In this sample: `GetProposalsQuery`, `GetMeetingsQuery`,
`GetSubscriptionsQuery`, … in `Application/Queries/<UseCase>/`. Handlers read
through the module's repositories/`DbContext` and return DTO records from
`Application/Dtos` — never domain entities, so persistence details and lazy
loading can never leak into responses.
