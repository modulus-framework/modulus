---
sidebar_position: 1
---

# Deployment

Modulus applications deploy as a single process.

## Deployment Options

| Option | Description |
|--------|-------------|
| **Self-contained** | Includes .NET runtime |
| **Framework-dependent** | Requires .NET 10 runtime |
| **Docker** | Containerized deployment |
| **Azure App Service** | Managed hosting |
| **AWS ECS/Lambda** | Container or serverless |

## Publish

```bash
# Framework-dependent
dotnet publish src/API/MyApp.Api -c Release -o ./publish

# Self-contained
dotnet publish src/API/MyApp.Api -c Release --self-contained -o ./publish

# With trimming
dotnet publish src/API/MyApp.Api -c Release --self-contained -p:PublishTrimmed=true -o ./publish
```

## Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/API/MyApp.Api -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MyApp.Api.dll"]
```

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `ASPNETCORE_ENVIRONMENT` | Environment name |
| `ASPNETCORE_URLS` | Listen URLs |
| `ConnectionStrings__*` | Database connections |
| `DOTNET_EnableDiagnostics` | Enable diagnostics |

## Health Checks

```bash
# Liveness
curl https://your-app/health/live

# Readiness
curl https://your-app/health/ready
```

## Database Migrations

```bash
# At startup (automatic)
await app.Services.MigrateModulusDatabasesAsync();

# Or manually
modulus migrate update
```

## Scaling

Since it's a single process:

- **Horizontal**: Run multiple instances behind a load balancer
- **State**: Use distributed cache (Redis) and outbox for consistency
- **SignalR**: Use backplane (Redis/Azure) for multi-instance

## See Also

- [Build System](configuration/build-system) — Build configuration
- [Health Checks](hardening/health-checks) — Monitoring
