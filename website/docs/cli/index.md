---
sidebar_position: 1
---

# CLI Reference

<img src="/img/icon.png" alt="Modulus CLI icon" width="96" />

The `modulus` CLI tool generates complete solutions, modules, and CRUD code.

## Installation

```bash
dotnet tool install -g --add-source ./nupkg Synvro.Modulus.Cli
```

## Commands

| Command | Description |
|---------|-------------|
| [`modulus app`](app) | Create a new application |
| [`modulus module`](module) | Create a blank module |
| [`modulus add-module`](add-module) | Add a module to existing app |
| [`modulus generate-crud`](generate-crud) | Generate CRUD operations |
| [`modulus generate-command`](generate-command) | Generate a command |
| [`modulus generate-query`](generate-query) | Generate a query |
| [`modulus migrate`](migrate) | Database migrations |
| [`modulus list`](list) | List modules and entities |
| [`modulus info`](info) | Show app overview |
| [`modulus doctor`](doctor) | Check environment |
| [`modulus ui`](../ui/cli) | Scaffold pages, components and themes for the UI (`add-admin`, `create-crud`, `create-dashboard`, ...) |
| [`modulus outdated`](outdated) | Check for package updates |
| [`modulus update`](update) | Apply package updates |
| `modulus describe` | Print the app's kind, hosts, modules, BFFs and wired features (`--json` for tools) |
| `modulus add-bff <client>` | Add a `web`, `mobile` or `partner` backend-for-frontend host (see [BFF](../platform/bff)) |
| `modulus generate-bff-endpoint <Name>` | Scaffold an aggregate endpoint in a BFF |
| `modulus generate-grpc <Entity>` | Expose an entity's CRUD over gRPC (see [gRPC](../platform/grpc)) |
| `modulus generate-graphql <Entity>` | Expose an entity's CRUD over GraphQL (see [GraphQL](../platform/graphql)) |
| `modulus add-realtime` | Push integration events to clients over SSE or SignalR (see [Realtime](../platform/realtime)) |
| `modulus add-webhooks` | Deliver integration events to signed external endpoints (see [Webhooks](../messaging/webhooks)) |
| `modulus add-ai` | Host the AI platform connector (see [AI Connector](../ai/connector)) |
| `modulus add-audit-store` | Keep the business and security audit logs in a database (see [Security Model](../security/overview)) |
| `modulus add-tenant-lifecycle` | Add tenant exports, legal holds and deletion to a multi-tenant app |
| `modulus add-analytics` | Add the analytics store and its local services |
| `modulus add-openbao` | Read secrets from OpenBao |
| `modulus ui ...` | UI scaffolding, see [the UI section](../ui/cli) |

## Global Flags

| Flag | Description |
|------|-------------|
| `--dry-run` | Preview without writing files |
| `--force` | Overwrite without prompting |
| `-v, --verbose` | Detailed output |
| `-q, --quiet` | Suppress non-essential output |
