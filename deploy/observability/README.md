# Observability stack

Grafana, Loki, Tempo and Prometheus behind an OpenTelemetry Collector, for Modulus applications. The applications export traces,
metrics and logs over OTLP (`AddModulusOpenTelemetry`, settings `OpenTelemetry:Otlp`); the collector routes each signal to its backend.

| Signal | Path | Backend | Grafana data source |
|---|---|---|---|
| Traces | collector → Tempo (OTLP gRPC) | Tempo | Tempo |
| Metrics | collector → Prometheus (remote write) | Prometheus | Prometheus |
| Logs | collector → Loki (OTLP HTTP at `/otlp`) | Loki | Loki |

Log records carry the trace id, so a Loki line links to its Tempo trace (derived field on the Loki data source), and a trace links back to its logs.

## Start

```
docker compose -f deploy/observability/docker-compose.yml up -d
```

Podman works too: `podman-compose -f deploy/observability/docker-compose.yml up -d`.

## Point an application at it

```json
"OpenTelemetry": {
  "ServiceName": "orders-api",
  "Otlp": { "Endpoint": "http://localhost:4317" }
}
```

The application sends all three signals to the collector through `Otlp:Endpoint`. Use `Otlp:Traces:Endpoint`, `Otlp:Metrics:Endpoint` or
`Otlp:Logs:Endpoint` to send a signal elsewhere. Logs go over OTLP only when an exporter is configured for them.

## Dashboards and alerts

- Grafana (http://localhost:3000) provisions the data sources and the `Modulus` folder. The messaging dashboard is in `grafana/dashboards`.
- Prometheus loads the messaging alert rules from `prometheus/rules`.

## Before production

- Change the Grafana admin password (it is a development value in `docker-compose.yml`) and put Grafana behind TLS and your identity provider.
- Replace the filesystem storage of Loki and Tempo with object storage, and size retention for your volume.
- Put the collector behind TLS for applications outside the host network.
- The images are pinned. Upgrade them deliberately and run the integration test (`tests/integration/Modulus.Observability.Stack.Integration.Tests`) against the new versions.

## Licences

Grafana, Loki and Tempo are AGPL-3.0; Prometheus and the OpenTelemetry Collector are Apache-2.0. They run as separate services; Modulus does not link them.
