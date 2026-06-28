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
