#!/usr/bin/env bash
# NUKE-style entry point for the Meetup sample (Linux / macOS / Git Bash).
# Thin wrapper over the dotnet CLI — no extra dependencies.
# See build.ps1 for why full NUKE was deliberately not added.
#
#   ./build.sh [Clean|Build|Format|Test|Run] [Debug|Release]
set -euo pipefail

TARGET="${1:-Build}"
CONFIGURATION="${2:-Debug}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

step() { echo -e "\n==> $1"; }

case "$TARGET" in
  Clean)
    step "Clean bin/obj"
    find "$ROOT" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
    ;;
  Build)
    step "Build Meetup.slnx ($CONFIGURATION)"
    dotnet build "$ROOT/Meetup.slnx" -c "$CONFIGURATION"
    ;;
  Format)
    step "Verify formatting (no writes)"
    dotnet format "$ROOT/Meetup.slnx" --verify-no-changes
    ;;
  Test)
    # No test projects ship with the sample yet (see README §8 Roadmap).
    echo "No test projects yet — see README §8 Roadmap item 2." >&2
    exit 1
    ;;
  Run)
    step "Run Meetup.Web ($CONFIGURATION)"
    dotnet run --project "$ROOT/src/Web/Meetup.Web/Meetup.Web.csproj" -c "$CONFIGURATION" --no-build
    ;;
  *)
    echo "Unknown target: $TARGET (Clean|Build|Format|Test|Run)" >&2
    exit 1
    ;;
esac
