---
sidebar_position: 1
---

# Build System

Modulus uses MSBuild with centralized configuration.

## Directory.Build.props

```
modulus/
├── Directory.Build.props              # Root (imports build/*.props)
├── build/
│   ├── Modulus.Common.props           # Compilation settings
│   ├── Modulus.Packaging.props        # NuGet packaging
│   └── Modulus.Test.props             # Test project overrides
├── src/
│   └── Directory.Build.props          # Marks as packable
└── tests/
    └── Directory.Build.props          # Chains to root + test props
```

## Key Settings

| Setting | Value |
|---------|-------|
| TargetFramework | `net10.0` |
| Nullable | `enable` |
| ImplicitUsings | `enable` |
| TreatWarningsAsErrors | `true` |
| GenerateDocumentationFile | `true` |

## Global Usings

```csharp
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
```

## NuGet Packaging

| Setting | Value |
|---------|-------|
| Authors | Cobytelabs |
| VersionPrefix | 1.2.0 |
| License | Apache-2.0 |
| Source Link | GitHub |
| Symbols | snupkg |

## Common Commands

```bash
# Build (Debug)
dotnet build modulus.slnx

# Build (Release)
dotnet build modulus.slnx -c Release

# Pack NuGet packages
dotnet pack modulus.slnx -c Release

# Format check
dotnet format modulus.slnx --verify-no-changes

# Format (apply)
dotnet format modulus.slnx
```

## See Also

- [Central Package Management](central-package-management) — Dependency versions
