---
sidebar_position: 1
---

# Modulus.Messaging

Modulus.Messaging is the bus that Modulus applications use for messages between services and between modules. It is a replacement for MassTransit with the same consumer, publish, send, saga and request model, and it runs on three transports: PostgreSQL, RabbitMQ and an in-memory bus for tests.

## What it gives you

- **Consumers and publish.** Register a consumer for a message type on an endpoint, publish to every subscriber, or send to one endpoint.
- **Reliability.** Retries, delayed redeliveries, an `_error` queue for failed messages and a `_skipped` queue for messages that cannot run, a transactional outbox, and an idempotent inbox.
- **Sagas.** State machine sagas and consumer sagas, with requests, schedules and request timeouts.
- **Transports.** PostgreSQL (one queue table, row locks, durable), RabbitMQ (quorum queues, publisher confirms, reconnect back-off) and an in-memory transport for development and tests.
- **Operations.** OpenTelemetry metrics and traces, health checks, an operator endpoint to replay parked messages, and alert rules and a dashboard.
- **Contracts.** Each message has a stable wire identity (its URN), checked in CI, and an analyzer that flags messages without one.

## Pages

- [Quick start](quick-start): a consumer and a publish, per transport.
- [Configuration](configuration): every setting and its default.
- [Sagas](sagas): state machines, consumer sagas, requests and request timeouts.
- [URNs and rolling upgrades](urns-and-upgrades): message identity, the name formatter, the legacy bridge and the analyzer.
- [Operations runbook](operations): parked messages, outages, scheduled messages and the schema rollout.
- [Monitoring](monitoring): metrics, alert rules and the dashboard.
- [Migration from MassTransit](migration): what maps to what, and what changes.

## Adding it to an application

Run `modulus add-messaging` in an API app generated with Modulus, or follow the [quick start](quick-start). The [CLI page](../../cli/add-messaging) lists what the command changes.
