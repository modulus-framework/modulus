#!/usr/bin/env bash
# Downloads every package in a directory from nuget.org and checks its signature with `dotnet nuget verify --all`.
# Usage: build/verify-published-packages.sh <packages-dir>
# Indexing on nuget.org can lag the push, so a download is retried (PUBLISH_VERIFY_ATTEMPTS x PUBLISH_VERIFY_DELAY seconds).
set -euo pipefail

dir="${1:?usage: verify-published-packages.sh <packages-dir>}"
attempts="${PUBLISH_VERIFY_ATTEMPTS:-20}"
delay="${PUBLISH_VERIFY_DELAY:-30}"
base="https://api.nuget.org/v3-flatcontainer"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

status=0
shopt -s nullglob
for pkg in "$dir"/*.nupkg; do
  nuspec="$(unzip -p "$pkg" '*.nuspec')"
  id="$(printf '%s' "$nuspec" | sed -n 's:.*<id>\(.*\)</id>.*:\1:p' | head -n1)"
  version="$(printf '%s' "$nuspec" | sed -n 's:.*<version>\(.*\)</version>.*:\1:p' | head -n1)"
  if [ -z "$id" ] || [ -z "$version" ]; then
    echo "::error::Cannot read id and version from $pkg"
    status=1
    continue
  fi

  # The flat container addresses packages by lower-case id and version.
  lid="${id,,}"
  lversion="${version,,}"
  url="$base/$lid/$lversion/$lid.$lversion.nupkg"
  target="$work/$lid.$lversion.nupkg"

  downloaded=false
  for attempt in $(seq 1 "$attempts"); do
    if curl -fsSL "$url" -o "$target"; then
      downloaded=true
      break
    fi
    echo "Waiting for $id $version on nuget.org (attempt $attempt of $attempts)"
    sleep "$delay"
  done
  if [ "$downloaded" != true ]; then
    echo "::error::$id $version was not available on nuget.org after $attempts attempts"
    status=1
    continue
  fi

  if dotnet nuget verify "$target" --all; then
    echo "Signature verified: $id $version"
  else
    echo "::error::Signature check failed for the published $id $version"
    status=1
  fi
done

exit "$status"
