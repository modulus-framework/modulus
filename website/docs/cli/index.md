---
sidebar_position: 1
---

# CLI Reference

<img src="/img/icon.png" alt="Modulus CLI icon" width="96" />

The `modulus` CLI tool generates complete solutions, modules, and CRUD code.

## Installation

```bash
dotnet tool install -g --add-source ./nupkg Cobytelabs.Modulus.Cli
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
| [`modulus outdated`](outdated) | Check for package updates |
| [`modulus update`](update) | Apply package updates |

## Global Flags

| Flag | Description |
|------|-------------|
| `--dry-run` | Preview without writing files |
| `--force` | Overwrite without prompting |
| `-v, --verbose` | Detailed output |
| `-q, --quiet` | Suppress non-essential output |
