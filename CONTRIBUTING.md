# Contributing to Modulus Framework

Thank you for your interest in contributing to Modulus Framework! This document outlines the process for contributing.

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/YOUR-USERNAME/modulus.git`
3. Create a feature branch: `git checkout -b feature/my-feature`
4. Build the solution: `dotnet build modulus.slnx`
5. Run tests: `dotnet test modulus.slnx`

## Development Requirements

- .NET 10 SDK (10.0.109 or later)
- Visual Studio 2022, JetBrains Rider, or VS Code

## Code Style

- **TreatWarningsAsErrors** is enabled — all code must compile with zero warnings
- Use **file-scoped namespaces** (`namespace Foo;`)
- Use **primary constructors** where applicable
- Use **collection expressions** (`[]` instead of `new List<T>()`)
- Use **sealed** classes by default
- Enable **Nullable reference types** (`<Nullable>enable</Nullable>`)
- Use **XML documentation comments** on all public members
- Follow the **REPR pattern** for endpoints (no controllers)

## Pull Request Process

1. Ensure your code builds: `dotnet build modulus.slnx` (0 errors, 0 warnings)
2. Add or update tests for your changes
3. Update documentation if needed
4. Squash your commits
5. Open a pull request with a clear description

## Public API & Breaking Changes

Every packable project under `src/` (and the CLI) tracks its public surface
with [`Microsoft.CodeAnalysis.PublicApiAnalyzers`](https://github.com/dotnet/roslyn-analyzers/blob/main/src/PublicApiAnalyzers/PublicApiAnalyzers.Help.md):
a `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` sit next to each
`.csproj`, and the build fails (`RS0016`/`RS0017`, errors under
`TreatWarningsAsErrors`) if a public member is added or removed without a
matching entry. This is enforcement, not paperwork — a "1.4.0" that claims
SemVer needs *something* stopping a public signature from changing silently
behind it, and this is that something.

- **Adding public API**: build the project — the analyzer reports the new
  member as undeclared. Add it to `PublicAPI.Unshipped.txt` yourself, or let
  the codefix do it: `dotnet format analyzers <project>.csproj --diagnostics RS0016 --severity info`.
- **Removing or changing public API**: same mechanism catches it (`RS0017`
  for a removed member); update the file to match. Think about whether the
  change is source- and binary-compatible before you do — if it isn't, it's
  a breaking change (see below), not a routine edit.
- **Cutting a release**: everything in every project's `Unshipped.txt` moves
  to `Shipped.txt` as part of tagging that version — that file is the
  permanent record of what a given package version actually shipped.

**Breaking changes** (removing a public member, changing a signature in an
incompatible way, tightening a previously-loose contract) require a major
version bump per SemVer and must be called out explicitly in `CHANGELOG.md`,
not folded quietly into a minor/patch entry. Prefer not to ship one at all:
mark the old member `[Obsolete("...", error: false)]` pointing at its
replacement for at least one minor version before removing it, so consumers
get a compile-time warning instead of a broken build on upgrade.

## Branching Strategy

- `main` — stable release branch
- `develop` — active development branch
- `feature/*` — feature branches
- `bugfix/*` — bug fix branches

## Commit Convention

Use conventional commits:
- `feat:` new feature
- `fix:` bug fix
- `docs:` documentation
- `refactor:` code refactoring
- `test:` tests
- `chore:` build/tooling

## Reporting Issues

- Use GitHub Issues
- Include repro steps, expected vs actual behavior
- Specify .NET SDK version and Modulus version
