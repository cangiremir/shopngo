# Operational Reliability Runbook

Practical guide to detect, triage, and recover failures in the local production-like `ShopNGo` stack.

## Quick Navigation

- [Live Endpoints](#live-endpoints)
- [Fast Triage Flow](#fast-triage-flow)
- [Alert-to-Action Map](#alert-to-action-map)
- [Queue and Health Checks](#queue-and-health-checks)
- [Recovery Playbooks](#recovery-playbooks)
- [Failure Drills](#failure-drills)
- [Evidence Checklist](#evidence-checklist)

## Live Endpoints

- RabbitMQ UI: `http://localhost:15672` (`guest` / `guest`)
- RabbitMQ metrics: `http://localhost:15692/metrics`
- Prometheus: `http://localhost:9090`
- Alertmanager: `http://localhost:9093`
- Grafana: `http://localhost:3000` (`admin` / `admin`)
- Jaeger: `http://localhost:16686`
- Order API: `http://localhost:8081`
- Stock API: `http://localhost:8082`
- Notification API: `http://localhost:8083`

## Fast Triage Flow

1. Identify the failing signal:
   - Which alert fired?
   - Which queue/service is affected?
2. Check service readiness:
   - `Order`, `Stock`, `Notification` on `/health/ready`
3. Check queue state:
   - `*.retry`, `*.dlq`, and `*_error`
4. Correlate message context:
   - `messageId`, `correlationId`, trace/log entries
5. Recover dependency first:
   - broker/db/service health
6. Replay only after root cause is fixed.

## Alert-to-Action Map

| Alert                                                        | What it means                     | First action                                              |
| ------------------------------------------------------------ | --------------------------------- | --------------------------------------------------------- |
| `ShopNGoDlqBacklogNonZero` / `ShopNGoDlqBacklogCritical`     | Dead-letter backlog exists        | Inspect payload/properties, fix root cause, replay safely |
| `ShopNGoRetryBacklogStuck`                                   | Retry queue is not draining       | Check consumer readiness + DB/broker connectivity         |
| `ShopNGoServiceReadinessDown` / `ShopNGoServiceLivenessDown` | Service probe failure             | Check container status and service logs                   |
| `ShopNGoTechnicalFailuresElevated`                           | Technical exceptions increased    | Identify failing queue and dependency bottleneck          |
| `ShopNGoOutboxDispatchFailuresElevated`                      | Outbox publish failures increased | Validate RabbitMQ connectivity/auth/routing               |

## Queue and Health Checks

Priority queues:

- `stock.order-created`
- `stock.order-created.retry`
- `stock.order-created.dlq`
- `stock.order-created_error` (MassTransit fault queue)
- `order.stock-reserved*`
- `order.stock-rejected*`
- `notification.order-confirmed*`
- `notification.order-rejected*`

Queue detail:

```powershell
Invoke-RestMethod -Uri http://localhost:15672/api/queues/%2F/stock.order-created.dlq -Headers @{
  Authorization = "Basic $([Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes('guest:guest')))"
}
```

Readiness:

```powershell
Invoke-WebRequest http://localhost:8081/health/ready
Invoke-WebRequest http://localhost:8082/health/ready
Invoke-WebRequest http://localhost:8083/health/ready
```

Liveness:

```powershell
Invoke-WebRequest http://localhost:8081/health/live
Invoke-WebRequest http://localhost:8082/health/live
Invoke-WebRequest http://localhost:8083/health/live
```

## Recovery Playbooks

### Replay a Fault Message

1. Fix root cause first (DB/broker/service).
2. Dry run:

```powershell
pwsh ./platform/chaos/05-dlq-replay.ps1 -DryRun
```

3. Replay:

```powershell
pwsh ./platform/chaos/05-dlq-replay.ps1,
```

4. Verify order reaches terminal status (`Confirmed` or `Rejected`).

Note: replay script checks `stock.order-created.dlq` first, then `stock.order-created_error`.

### RabbitMQ Outage Recovery

1. Start RabbitMQ.
2. Verify management API/UI is responsive.
3. Verify service readiness.
4. Confirm outbox backlog drains.
5. Confirm `PendingStock` orders finalize.

### Stock DB Outage Recovery

1. Start `stock-db`.
2. Verify `stock-service` readiness.
3. Confirm fault queue growth has stopped.
4. Replay affected messages.
5. Confirm impacted orders finalize.

## Reliability Drills

1. Poison message to fault queue:

```powershell
pwsh ./platform/chaos/01-poison-message-dlq.ps1
```

2. RabbitMQ outage and outbox recovery:

```powershell
pwsh ./platform/chaos/02-rabbitmq-outage-outbox-recovery.ps1
```

3. Stock DB outage with retry/fault recovery:

```powershell
pwsh ./platform/chaos/03-stock-db-outage-retry-dlq.ps1
```

4. Duplicate delivery idempotency:

```powershell
pwsh ./platform/chaos/04-duplicate-delivery-idempotency.ps1
```

5. DLQ/fault replay utility:

```powershell
pwsh ./platform/chaos/05-dlq-replay.ps1
```

All drills support `-DryRun`.

## Evidence Checklist

Capture these for each incident or drill:

- Alert name + timestamp
- Queue counts before/after
- `messageId` + `correlationId`
- Relevant error logs (`errorCode`, retry/fault behavior)
- Grafana panel snapshot (queue depth/failure/health)
- Jaeger trace ID (if available)
