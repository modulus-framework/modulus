---
sidebar_position: 2
---

# Central Package Management

Modulus uses NuGet Central Package Management (CPM) for dependency version control.

## Directory.Packages.props

All package versions are defined in `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.9" />
    <PackageVersion Include="OpenIddict.AspNetCore" Version="7.5.0" />
    <PackageVersion Include="FluentValidation" Version="12.1.1" />
    <!-- ... -->
  </ItemGroup>
</Project>
```

## Usage in Projects

Projects reference packages without versions:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore" />
  <PackageReference Include="FluentValidation" />
</ItemGroup>
```

## Adding a New Package

1. Add `<PackageVersion>` in `Directory.Packages.props`
2. Add `<PackageReference>` in the project file

## Version Upgrades

1. Update `<PackageVersion>` in `Directory.Packages.props`
2. Build to verify compatibility
3. Run tests

## Key Dependencies

| Category | Package | Version |
|----------|---------|---------|
| **EF Core** | Microsoft.EntityFrameworkCore | 10.0.9 |
| **Identity** | OpenIddict.AspNetCore | 7.5.0 |
| **Validation** | FluentValidation | 12.1.1 |
| **Testing** | xunit | 2.9.3 |
| **Messaging** | RabbitMQ.Client | 7.2.1 |
| **Observability** | OpenTelemetry | 1.16.0 |

## See Also

- [Build System](build-system) — Compilation settings
