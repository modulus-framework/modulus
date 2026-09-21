# 0012. One type per file, use-case folders

Date: 2026-09-15

Status: accepted

## Context

Early versions of this sample bundled whole features into single files
(`MeetingCommands.cs`, `MeetingEndpoints.cs`), which made navigation, review,
and merge-conflict rates worse as the sample grew.

## Decision

One type per file (the only exception: an endpoint and its own request DTO are
co-located). Commands/queries live in per-use-case folders —
`Commands/<UseCase>/<UseCase>Command.cs` + `…Handler.cs` + `…Validator.cs`
(when rules exist) — and likewise DTOs, events, entities, enums,
repositories, and endpoints each get their own file.

## Consequences

- More files, but each is small, greppable, and conflict-free; matches the
  TradeFlow sample and the `modulus generate-crud` output shape.
- Contributors must split rather than append when adding a use case.
