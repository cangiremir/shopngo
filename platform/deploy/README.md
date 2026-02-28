# Deployment Scaffold

This folder contains production-oriented deployment scaffolding for Docker Compose environments.

## Structure

- `environments/*.env.example`: environment templates (no real secrets)
- `compose/docker-compose.prod.yml`: production override for `docker-compose.yml`
- `scripts/deploy.ps1`: config validation + deploy
- `scripts/rollback.ps1`: targeted service image rollback

## Quick Start

1. Copy an environment template and fill real values:
   - `platform/deploy/environments/prod.env.example` -> `platform/deploy/environments/prod.env`
2. Deploy:
   - `pwsh ./platform/deploy/scripts/deploy.ps1 -Environment prod -PullImages`
3. Roll back with previous image tags (full image refs):
   - `pwsh ./platform/deploy/scripts/rollback.ps1 -Environment prod -OrderServiceImage ghcr.io/org/shopngo-order-service:<tag> -StockServiceImage ghcr.io/org/shopngo-stock-service:<tag> -NotificationServiceImage ghcr.io/org/shopngo-notification-service:<tag>`

## Notes

- Keep `*.env` files out of git.
- Use GitHub Environment secrets/variables in CI for real production values.
- RabbitMQ consumer throughput is profile-based (`Conservative`, `Balanced`, `Aggressive`) via env vars:
  - `ORDER_RABBITMQ_THROUGHPUT_LEVEL`, `STOCK_RABBITMQ_THROUGHPUT_LEVEL`, `NOTIFICATION_RABBITMQ_THROUGHPUT_LEVEL`
  - endpoint-specific overrides for hot paths (for example `STOCK_RABBITMQ_ENDPOINT_ORDER_CREATED_LEVEL`).
