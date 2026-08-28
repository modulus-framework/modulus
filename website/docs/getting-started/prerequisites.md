---
sidebar_position: 1
---

# Prerequisites

## Required

| Requirement | Version | Check |
|-------------|---------|-------|
| **.NET SDK** | 10.0.109+ | `dotnet --version` |
| **Node.js** | 20+ | `node --version` |

## Optional

| Tool | Purpose | Install |
|------|---------|---------|
| **Docker** | Integration tests (Testcontainers) | [docker.com](https://docker.com) |
| **dotnet-ef** | EF Core migrations via CLI | `dotnet tool install -g dotnet-ef` |
| **dbsh** | SQL-first migrations (alternative to EF Core) | `dotnet tool install -g dbsh` |

## Verify Your Setup

```bash
dotnet --version        # Should show 10.0.109 or newer
dotnet --list-sdks      # Should list a 10.0.x SDK
docker --version        # Optional, for integration tests
```

## Supported Platforms

- Windows (x64, ARM64)
- macOS (x64, ARM64)
- Linux (x64, ARM64)

## IDE Support

Any editor with C# support works. Recommended:

- **Visual Studio 2022** 17.x+ (with .NET 10 workload)
- **JetBrains Rider** 2025.x+
- **VS Code** with the C# Dev Kit extension
