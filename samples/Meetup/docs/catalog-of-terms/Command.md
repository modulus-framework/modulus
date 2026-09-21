# Command

An object describing the intent to change state, handled exactly once by a
single command handler.

In this sample every write is a command record
(`ProposeMeetingGroupCommand`, `BuySubscriptionCommand`, …) in
`Application/Commands/<UseCase>/`, validated by FluentValidation in the
mediator pipeline and executed by `<UseCase>Handler` against the domain model.
Commands may return small results (new id, status) — see ADR-0008.
