---
sidebar_position: 6
---

# Monitoring

The bus emits OpenTelemetry metrics, and the PostgreSQL transport adds a queue-depth gauge. The names below are the OpenTelemetry names; a Prometheus exporter maps them (dots become underscores, counters get `_total`, a histogram in seconds gets `_seconds`).

| Metric | Type | Meaning |
|---|---|---|
| `messaging.parked.messages` | counter, label `messaging.destination.name` | A message moved to `_error` or `_skipped`. |
| `messaging.process.duration` | histogram, seconds | Time a consume attempt takes. |
| `messaging.client.consumed.messages` | counter | Messages consumed. |
| `messaging.client.sent.messages` | counter | Messages sent or published. |
| `messaging.inbox.duplicates` | counter | Redeliveries skipped because the message was already applied. |
| `modulus.messaging.queue.depth` | gauge, label `queue` | Rows waiting in each queue (PostgreSQL transport). |

## Alert rules

These rules are in the repository as `docs/messaging/monitoring/prometheus-alerts.yml`. Check the metric names against your exporter once before you load them.

```yaml
# Alert rules for Modulus.Messaging. Metric names follow the OpenTelemetry-to-Prometheus mapping (dots become underscores,
# counters get _total, a histogram unit of seconds gets _seconds). Check the names against your exporter before you load
# the file: run `curl <app>/metrics | grep messaging_` once.
groups:
  - name: modulus-messaging
    rules:
      - alert: MessagingParkedMessages
        expr: sum by (messaging_destination_name) (increase(messaging_parked_messages_total[15m])) > 0
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Messages are parked in _error or _skipped on {{ $labels.messaging_destination_name }}"
          runbook: "docs/messaging/operations.md#parked-messages"

      - alert: MessagingParkedMessagesBurst
        expr: sum by (messaging_destination_name) (increase(messaging_parked_messages_total[10m])) > 50
        for: 2m
        labels:
          severity: critical
        annotations:
          summary: "More than 50 messages parked in 10 minutes on {{ $labels.messaging_destination_name }}"
          runbook: "docs/messaging/operations.md#parked-messages"

      - alert: MessagingSlowConsumers
        expr: histogram_quantile(0.95, sum by (le, messaging_destination_name) (rate(messaging_process_duration_seconds_bucket[10m]))) > 5
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "p95 consume time above 5 s on {{ $labels.messaging_destination_name }}"
          runbook: "docs/messaging/operations.md#outages"

      - alert: MessagingThroughputStopped
        expr: sum(rate(messaging_client_consumed_messages_total[10m])) == 0 and sum(rate(messaging_client_sent_messages_total[10m])) > 0
        for: 10m
        labels:
          severity: critical
        annotations:
          summary: "Messages are sent but none are consumed"
          runbook: "docs/messaging/operations.md#outages"

      - alert: MessagingRedeliveriesPiling
        expr: sum(rate(messaging_inbox_duplicates_total[10m])) > 1
        for: 15m
        labels:
          severity: warning
        annotations:
          summary: "Redeliveries are skipped by the inbox at more than 1 per second"
          runbook: "docs/messaging/operations.md#parked-messages"

      - alert: MessagingQueueDepthHigh
        expr: max by (queue) (modulus_messaging_queue_depth) > 10000
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "Queue {{ $labels.queue }} holds more than 10,000 rows"
          runbook: "docs/messaging/operations.md#outages"
```

## Dashboard

A Grafana dashboard with six panels over the same metrics is available as a JSON file: [grafana-dashboard.json](/messaging/grafana-dashboard.json). Import it and choose your Prometheus data source.
