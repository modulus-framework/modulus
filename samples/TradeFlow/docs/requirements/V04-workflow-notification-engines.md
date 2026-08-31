# V04 — Workflow Engine & Notification Engine
Covers master topics: 07, 08

---

# PART A — WORKFLOW ENGINE (topic 07)

## A1. Objectives
One engine routes every approval and process orchestration UI-side (sagas handle system-side orchestration, V27): PR, PO, LC, BoE variance, vendor onboarding, contract, cost-sheet finalization, budget transfer, DoA edits — all as **configuration, not code**. Tenants design, simulate, version, and publish workflows without vendor involvement.

## A2. Definition Model
Versioned JSON documents in `platform.workflow_definition(id, tenant_id, key, version, status[Draft|Published|Retired], definition jsonb, published_by/at)`. In-flight instances are pinned to their version forever (WF-01).

```jsonc
{
  "key": "po-approval",
  "trigger": { "document": "purchase_order", "on": "Submitted" },
  "context": ["amount_bdt", "category_id", "org_path", "is_import", "feasibility_score", "budget_status"],
  "steps": [
    { "id": "feasibility-gate", "type": "condition",
      "when": "is_import && feasibility_score < policy('feasibility.threshold', 60)",
      "then": "cfo-override", "else": "doa-chain" },
    { "id": "doa-chain", "type": "approval-chain",
      "resolver": "doa", "matrix": "po",            // DoA slabs resolve approver positions
      "sla_hours": 24, "escalation": { "after_hours": 24, "to": "manager-of", "then_after": 24, "to_role": "tenant-admin" } },
    { "id": "cfo-override", "type": "approval",
      "assignee": { "role": "CFO" }, "require_reason": true,
      "capability": "po.override_feasibility", "sla_hours": 48 },
    { "id": "notify-vendor", "type": "system-action", "action": "po.dispatch" }
  ],
  "on_reject": { "return_to": "requester", "allow_resubmit": true, "max_resubmits": 3 },
  "on_timeout_final": "auto-reject-with-notice"
}
```

## A3. Step Type Registry
`approval` (single), `approval-chain` (DoA/org resolvers), `parallel` (all-of / any-of / quorum n-of-m), `condition` (expression over context; CEL-style, sandboxed, no I/O), `system-action` (whitelisted commands), `wait-event` (pause until domain event, e.g., budget released), `timer` (delay/deadline via EventBridge Scheduler), `sub-workflow` (composition), `human-task` (non-approval action item with form schema), `notification` (explicit send beyond defaults).

## A4. Assignment Resolvers
`position(code)`, `role(name, scope)`, `manager-of(initiator)`, `head-of(dept|bu|company)`, `doa(matrix, amount, org)`, `named-user` (discouraged, warns), `round-robin(role)`, `least-loaded(role)`. Resolution snapshot stored on the task (who/why-them) for audit; vacancy → skip/fallback rule per step, default: escalate to parent org node + admin alert.

## A5. Runtime Semantics
- Instance: `workflow_instance(id, definition_key, version, document_type/id, state, context jsonb, started_at)`; tasks: `workflow_task(id, instance_id, step_id, assignee_resolution jsonb, status[Open|Done|Skipped|Expired|Reassigned], acted_by, decision, reason, acted_at, due_at)`.
- **Idempotent & event-sourced-ish:** every transition appends `workflow_event`; instance state is a projection; replay-safe.
- Decisions: Approve | Reject | Return (to prior step or initiator, with comment mandatory) | Reassign (capability-gated) | Request-info (pauses SLA clock, pings initiator).
- **SoD enforced by engine:** actor ≠ document creator on approval steps unless step opts in (`allow_self: true`, warned at design time); actor ≠ prior-step actor on maker-checker pairs.
- Amount re-evaluation: if document is edited during Return→Resubmit and amount slab changes, chain **re-resolves from step 1** (WF-02, prevents slab-dodging via post-approval edits; already-approved identical slabs may fast-confirm per tenant policy).
- Concurrency: task claim is atomic (`UPDATE … WHERE status='Open'`); double-acting returns friendly conflict.
- Recall: initiator may recall while first step pending (configurable); post-first-approval recall requires `workflow.recall` capability, notifies prior approvers.

## A6. SLA, Escalation, Delegation
Per-step SLA → reminder at 50%/80% → escalation chain (resolver-based) → final timeout action (auto-reject | auto-approve [requires explicit risk-ack flag at design time, restricted to low-value matrices] | freeze+admin). Working-hours aware (tenant business calendar V25; Ramadan hours supported). Delegation (V03 D5) honored at resolution time; OOO auto-delegate rules per user with date range.

## A7. Designer, Simulation, Analytics
Visual designer (graph editor over the JSON), schema-validated save, **simulate mode**: feed synthetic document contexts ("75 L import PO from Site X by user Y") → engine returns resolved chain + SLAs without side effects; publish requires simulation-run receipt + checker approval (workflows are money-adjacent config). Analytics: cycle time per step, bottleneck heatmap, approval outcome mix, SLA breach league table, resubmit loops (design smell detector).

## A8. Edge Cases
Approver leaves company mid-task (offboarding wizard reassigns open tasks); document cancelled mid-flow (instance → Cancelled, tasks expire with notice); definition retired with in-flight instances (they finish on pinned version); circular manager references (design-time cycle detection); parallel branch deadlock (quorum unreachable after rejections → configurable collapse rule); clock skew on SLA timers (all UTC, rendered in tenant TZ Asia/Dhaka); mass reassignment (admin bulk tool with preview).

---

# PART B — NOTIFICATION ENGINE (topic 08)

## B1. Objectives
Every meaningful business moment reaches the right humans on the right channel in the right language without spamming them into filter-blindness. Notifications are **events × subscription rules × templates × channel adapters**, fully tenant-configurable.

## B2. Architecture
Domain events → EventBridge rule `notification-intents` → SQS → **Notification Service**: (1) match event against subscription rules; (2) resolve recipients (roles/positions/document-participants/explicit); (3) apply user preferences + quiet hours + digest settings; (4) render template (locale, channel variant); (5) dispatch via adapter; (6) record `notification_log` (status: Queued→Sent→Delivered→Read / Failed→Retried→DLQ) with provider receipts.

## B3. Channels (adapters)
| Channel | Provider strategy | Notes |
|---|---|---|
| In-app | SSE push + bell + inbox persistence | Always on; read receipts |
| Email | AWS SES; per-tenant branded sender (white label) | DKIM/SPF per custom domain; bounce/complaint suppression list |
| SMS | BD aggregator primary + secondary failover; masking-approved sender ID | Critical severity only by default; cost-metered |
| WhatsApp | WhatsApp Business Cloud API, pre-approved template registry | Template lifecycle managed in Admin; session vs template message logic; opt-in tracked per user (BTRC/GDPR-aware) |
| Push | FCM/APNs via PWA/native shells | Deep-links to task/document |
| Webhook | Tenant endpoints (V22) | For downstream systems |

## B4. Subscription & Severity Model
`notification_rule(tenant_id, event_key, audience jsonb, channels[], severity, template_key, throttle jsonb, enabled)`. Severity → default channel map: Info→in-app; Normal→in-app+email; High→in-app+email(+push); Critical→+SMS/WhatsApp, ignores quiet hours. Throttling: per-rule rate caps + **coalescing** ("12 documents expiring this week" digest instead of 12 emails) + storm breaker (event flood → auto-digest + admin alert).

## B5. Template System
`notification_template(key, channel, locale[en|bn], subject, body, variables jsonb_schema)`; Handlebars-style variables validated against event payload schema at save; per-tenant overrides layer above platform defaults; preview-with-sample in Admin; versioned; mandatory variables (document link, tenant name) enforced. WhatsApp templates additionally track Meta approval status.

## B6. Standard Event → Notification Matrix (platform defaults; excerpt — module volumes extend)
| Event | Audience | Severity |
|---|---|---|
| task.assigned / task.escalated | Assignee / escalation target | Normal / High |
| po.feasibility_below_threshold | Requester, CFO | High |
| lc.discrepancy_raised | TF team, Import exec, Finance Mgr | **Critical** |
| lc.maturity T-30/14/7/1 | Finance Mgr (+CFO at T-7) | Normal→High |
| container.demurrage_amber/red | Import exec, C&F | High/Critical |
| boe.assessment_variance_over_tolerance | Import exec, Customs lead | High |
| doc.expiry T-30/7 (permits, vendor docs, insurance) | Owner, Compliance | Digest |
| costsheet.ready_to_finalize | Finance Mgr | Normal |
| budget.threshold_80/95 | Budget owner | Normal/High |
| invoice.match_exception | AP clerk, Buyer | Normal |
| vendor.risk_tier_downgraded | Category Mgr, Buyer | High |
| security.new_device_login / ip_denied | User / Admin | High |
| workflow.sla_breached | Process owner, Admin | High |

## B7. User Preferences & Digests
Per-user matrix (event-category × channel), quiet hours (default 22:00–07:00 Asia/Dhaka; Critical pierces), daily/weekly digest composer (grouped by document type, deep links), language preference, mobile-only mode. Admin can mark rules "mandatory" (user cannot mute — e.g., security alerts, Critical finance).

## B8. Reliability & Compliance
At-least-once with idempotency keys (`event_id × recipient × channel`); retries with exponential backoff → DLQ → ops alert; provider failover for SMS; suppression lists honored; full audit trail retained 2 years hot / 6 years archive; PII minimization in payloads (IDs + links over embedded amounts on SMS); notification analytics dashboard (delivery rate, read rate per channel, cost per tenant for metered channels).

## B9. Edge Cases
Recipient resolves to zero users (rule misconfig → admin alert, never silent drop); user in multiple audiences (dedupe, highest severity wins); template variable missing at render (fallback template + error log, never send broken); WhatsApp template rejected by Meta mid-use (auto-fallback SMS/email + admin task); tenant SMS budget exhausted (downgrade to email + urgent admin notice, Critical security still sends platform-funded); clock-boundary digests during DST-free BD (fixed offset, trivial) but regional tenants with DST handled via IANA TZ per tenant.
