#!/usr/bin/env bash
# Container security scan of a generated Modulus API, the way it is deployed:
#   1. generates an API app from the packed framework, and its production deployment files (add-deployment --monitoring),
#   2. adds test secrets and certificates, and the migrations a Production start needs,
#   3. builds the image and scans it with Trivy (HIGH and CRITICAL vulnerabilities that have a fix fail the run),
#   4. runs the container in Production and scans its HTTP surface with OWASP ZAP (baseline; FAIL-level alerts fail the run).
# Requirements: the .NET 10 SDK, openssl, a container CLI (docker or podman), and a feed directory with the packed Synvro.Modulus.* packages.
# Usage: build/container-scan.sh <feed-dir> [work-dir]
#   CONTAINER_CLI=podman TRIVY_SOCKET=/run/user/1000/podman/podman.sock build/container-scan.sh artifacts/packages
# The secrets generated here are for the scan only; they are never written outside the work directory.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FEED="$(cd "${1:?usage: container-scan.sh <feed-dir> [work-dir]}" && pwd)"
WORK_ARG="${2:-$(mktemp -d)}"
mkdir -p "$WORK_ARG"
WORK="$(cd "$WORK_ARG" && pwd)"
CONTAINER="${CONTAINER_CLI:-docker}"
TRIVY_SOCKET="${TRIVY_SOCKET:-/var/run/docker.sock}"
TRIVY_IMAGE="aquasec/trivy:0.58.1"
ZAP_IMAGE="ghcr.io/zaproxy/zaproxy:stable"
CLI="$REPO/cli/Modulus.Cli.csproj"
APP_NAME="ScanApp"
APP="$WORK/$APP_NAME"
RUN="modulus-scan-$RANDOM$RANDOM"
PORT="${SCAN_PORT:-18080}"

cleanup() { "$CONTAINER" rm -f "$RUN-api" >/dev/null 2>&1 || true; }
trap cleanup EXIT

echo "==> generating $APP_NAME from $FEED"
dotnet run --project "$CLI" -- app "$APP_NAME" --auth openiddict --package-source "$FEED" --output "$WORK" --force --quiet
cd "$APP"

echo "==> production deployment files"
dotnet run --project "$CLI" -- add-deployment --host scan.example.com --email ops@example.com --monitoring

# The generated NuGet.config points at the feed by its absolute path on this machine, which the image build cannot see:
# the packages travel with the app instead.
mkdir -p feed
cp "$FEED"/*.nupkg feed/
sed -i 's#key="modulus-local" value="[^"]*"#key="modulus-local" value="feed"#' NuGet.config

echo "==> test secrets and certificates (scan only)"
PASS="Aa1!$(openssl rand -hex 12)"
mkdir -p deploy/certs
for name in signing encryption; do
  openssl req -x509 -newkey rsa:2048 -nodes -keyout "$WORK/$name.key" -out "$WORK/$name.crt" -days 2 -subj "/CN=scan-$name" 2>/dev/null
  openssl pkcs12 -export -out "deploy/certs/$name.pfx" -inkey "$WORK/$name.key" -in "$WORK/$name.crt" -passout "pass:$PASS"
done
chmod 644 deploy/certs/*.pfx
cp .env.example .env
set_env() { sed -i "s#^$1=.*#$1=$2#" .env; }
set_env Identity__Certificates__Signing__Password "$PASS"
set_env Identity__Certificates__Encryption__Password "$PASS"
set_env Identity__Seed__AdminEmail "admin@scan.example.com"
set_env Identity__Seed__AdminPassword "$PASS"
set_env Identity__Seed__PlatformOperatorEmail "admin@scan.example.com"
set_env GRAFANA_ADMIN_PASSWORD "$PASS"
set_env RESTIC_PASSWORD "$PASS"

echo "==> migrations (Production needs them; the generated app has none)"
dotnet restore "$APP_NAME.slnx"
mkdir -p "$WORK/tools"
dotnet tool install dotnet-ef --version 10.0.14 --tool-path "$WORK/tools" >/dev/null
PATH="$WORK/tools:$PATH" dotnet run --project "$CLI" -- migrate add InitialCreate

echo "==> building the image"
"$CONTAINER" build -t "$RUN:image" .

echo "==> Trivy: image vulnerabilities (HIGH and CRITICAL with a fix)"
"$CONTAINER" run --rm -v "$TRIVY_SOCKET:/var/run/docker.sock" "$TRIVY_IMAGE" \
  image --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 "$RUN:image"

echo "==> running the API in Production"
"$CONTAINER" run -d --name "$RUN-api" -p "127.0.0.1:$PORT:8080" --env-file .env \
  -v "$APP/deploy/certs:/app/certs:ro,z" "$RUN:image" >/dev/null
code=000
for _ in $(seq 1 90); do
  code="$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$PORT/health/live" || true)"
  [ "$code" = 200 ] && break
  sleep 2
done
if [ "$code" != 200 ]; then
  "$CONTAINER" logs "$RUN-api" 2>&1 | tail -40
  echo "the API did not become healthy (last status $code)" >&2
  exit 1
fi

echo "==> OWASP ZAP baseline"
set +e
"$CONTAINER" run --rm --network host -v "$REPO/security:/zap/conf:ro" -v "$WORK:/zap/wrk:rw" "$ZAP_IMAGE" \
  zap-baseline.py -t "http://127.0.0.1:$PORT/" -c /zap/conf/zap-baseline.conf -r zap-report.html -J zap-report.json -m 1
zap=$?
set -e
# Exit 0: no alerts; 2: warnings only (reported, not fatal); 1: at least one FAIL-level alert.
if [ "$zap" -ne 0 ] && [ "$zap" -ne 2 ]; then
  echo "ZAP reported FAIL-level alerts (exit $zap); see $WORK/zap-report.html" >&2
  exit 1
fi
echo "==> scan passed. Reports: $WORK/zap-report.html, $WORK/zap-report.json"
