# 0003. Use .NET 10 and C#

Date: 2026-09-15

Status: accepted

## Context

The sample needs a modern, LTS-aligned runtime with strong typing support for
DDD building blocks (records, required members, pattern matching).

## Decision

Target `net10.0` with C# latest, `Nullable` and `ImplicitUsings` enabled
everywhere, and `TreatWarningsAsErrors` on — any new warning fails the build.

## Consequences

- SDK 10.0.109+ required to build/run.
- Null-safety and style problems surface at compile time instead of review time.
