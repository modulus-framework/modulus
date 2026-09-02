---
sidebar_position: 13
---

# modulus update

Updates packages to their latest versions with backup and rollback support.

## Usage

```bash
modulus update [options]
```

## Options

| Option | Description | Default |
|--------|-------------|---------|
| `--dry-run` | Preview changes without modifying files | false |
| `--force` | Skip confirmation prompts | false |
| `--framework-only` | Only update `Cobytelabs.Modulus.*` packages | false |
| `-o, --output` | App root directory | Current directory |

## What It Does

1. Scans your project for package references
2. Queries NuGet V3 API for latest stable versions
3. Creates backups of modified files
4. Updates package versions in `.csproj` or `Directory.Packages.props`
5. Runs `dotnet restore` to verify compatibility
6. Rolls back on failure

## Update Process

```
┌─────────────────────────────────────────────────────────┐
│  1. Scan packages                                       │
├─────────────────────────────────────────────────────────┤
│  2. Check for updates                                   │
├─────────────────────────────────────────────────────────┤
│  3. Create backups (*.bak files)                        │
├─────────────────────────────────────────────────────────┤
│  4. Update .csproj / Directory.Packages.props           │
├─────────────────────────────────────────────────────────┤
│  5. Run dotnet restore                                  │
├─────────────────────────────────────────────────────────┤
│  6. Verify build succeeds                               │
└─────────────────────────────────────────────────────────┘
```

## Backup and Rollback

- **Backups**: Creates `.bak` files before modifying
- **Rollback**: Restores from `.bak` if `dotnet restore` fails
- **Cleanup**: Removes `.bak` files after successful update

## Example Output

```
────────────────────────── Modulus update ──────────────────────────

> Scanning 27 packages for updates...

Package                          Current    Latest     Update
────────────────────────────────────────────────────────────────────
Microsoft.EntityFrameworkCore     9.0.0      10.0.11    Major
Microsoft.Extensions.Caching      9.0.0      10.0.10    Major

Update 2 package(s)? [y/N]: y

> Updating packages...
  ✓ Microsoft.EntityFrameworkCore 9.0.0 → 10.0.11
  ✓ Microsoft.Extensions.Caching 9.0.0 → 10.0.10

> Running dotnet restore...
  ✓ Restore successful

> Cleaning up backups...
  ✓ Done

✓ 2 package(s) updated successfully.
```

## Examples

```bash
# Preview changes without modifying files
modulus update --dry-run

# Update framework packages only
modulus update --framework-only

# Update with confirmation prompts
modulus update

# Update without prompts
modulus update --force
```

## See Also

- [`outdated`](outdated) — Check for available updates
- [`doctor`](doctor) — Check environment health
