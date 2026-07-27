# Modulus Framework NuGet Packages

**READY FOR UPLOAD** — All 32 packages built with original `Modulus.*` IDs, 0 warnings, 0 errors.

## Package List

| # | Package | Dependencies |
|---|---------|-------------|
| 1 | `Modulus.Core` | *(none)* |
| 2 | `Modulus.Data.Abstractions` | Core |
| 3 | `Modulus.EntityFrameworkCore` | Data.Abstractions, Core |
| 4 | `Modulus.Data.SqlServer` | EFCore |
| 5 | `Modulus.Data.PostgreSQL` | EFCore |
| 6 | `Modulus.Data.MySQL` | EFCore |
| 7 | `Modulus.Data.SQLite` | EFCore |
| 8 | `Modulus.Data.MongoDB` | Data.Abstractions |
| 9 | `Modulus.Events` | Core |
| 10 | `Modulus.Mediator` | Core |
| 11 | `Modulus.Outbox.Abstractions` | *(none)* |
| 12 | `Modulus.Outbox` | Outbox.Abstractions, EFCore |
| 13 | `Modulus.Outbox.MongoDB` | Outbox.Abstractions, MongoDB |
| 14 | `Modulus.Inbox` | Events, EFCore |
| 15 | `Modulus.Inbox.MongoDB` | Events, MongoDB |
| 16 | `Modulus.EventBus.RabbitMQ` | Events |
| 17 | `Modulus.EventBus.Kafka` | Events |
| 18 | `Modulus.Sagas` | Events |
| 19 | `Modulus.Identity` | EFCore, Events |
| 20 | `Modulus.AspNetCore` | Core |
| 21 | `Modulus.Platform` | Core |
| 22 | `Modulus.Authorization.EntityFrameworkCore` | Platform, EFCore |
| 23 | `Modulus.Authorization.Management` | Platform |
| 24 | `Modulus.MultiTenancy.EntityFrameworkCore` | Platform, EFCore |
| 25 | `Modulus.Caching.Redis` | Platform |
| 26 | `Modulus.Storage.S3` | Platform |
| 27 | `Modulus.Storage.AzureBlobs` | Platform |
| 28 | `Modulus.SignalR.Backplane` | Platform |
| 29 | `Modulus.AspNetCore.Redis` | AspNetCore |
| 30 | `Modulus.Observability` | Core |
| 31 | `Modulus.Testing` | AspNetCore |
| 32 | `Modulus.Cli` | *(tool)* |

## Upload Command

```bash
# From repo root:
dotnet nuget push nupkg\*.nupkg -s https://api.nuget.org/v3/index.json -k YOUR_API_KEY --skip-duplicate
```

> **Important:** The `Modulus.*` prefix is reserved on nuget.org. You must push from the **modulus-framework** account that owns this prefix, or contact nuget support to transfer ownership.

## Package Metadata

- **License:** Apache-2.0
- **Repository:** https://github.com/modulus-framework/modulus
- **Version:** 1.0.0-alpha.1
- **Symbol packages:** included (.snupkg) for source debugging
