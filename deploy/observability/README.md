# Observability stack

Jaeger, VictoriaLogs and Prometheus behind an OpenTelemetry Collector, for Modulus applications. Every component is Apache-2.0
(see the open-source dependency policy in `AGENTS.md`). The applications export traces, metrics and logs over OTLP
(`AddModulusOpenTelemetry`, settings `OpenTelemetry:Otlp`); the collector routes each signal to its backend.

| Signal | Path | Backend | Web UI |
|---|---|---|---|
| Traces | collector → Jaeger (OTLP gRPC) | Jaeger v2 | http://localhost:16686 |
| Metrics | collector → Prometheus (remote write) | Prometheus | http://localhost:9090 |
| Logs | collector → VictoriaLogs (OTLP HTTP at `/insert/opentelemetry`) | VictoriaLogs | http://localhost:9428/select/vmui |

Log records carry the trace id, so a log line can be looked up by its trace in VictoriaLogs (`trace_id:"<id>"`), and the trace is
in Jaeger.

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

- There is no dashboard tool in the stack: use the Prometheus, Jaeger and VictoriaLogs UIs above. Perses (Apache-2.0) is the candidate
  if a dashboard tool is needed later.
- Prometheus loads the messaging alert rules from `prometheus/rules`. Alertmanager is not wired yet.
- The messaging dashboard in `docs/messaging/monitoring/grafana-dashboard.json` is a Grafana dashboard. It is not part of this stack.

## Before production

- Jaeger stores traces in memory (`jaeger/config.yaml`). For durable traces, replace the memory backend with a persistent one (Badger is Apache-2.0).
- VictoriaLogs and Prometheus keep 15 days on a Docker volume. Size retention for your volume.
- Put the collector behind TLS for applications outside the host network. Jaeger, VictoriaLogs and Prometheus have no authentication here.
- The images are pinned. Upgrade them deliberately and run the integration test (`tests/integration/Modulus.Observability.Stack.Integration.Tests`) against the new versions.
