# ShopNGo Load Validation

This folder contains the k6-based concurrency/load check for the stock reservation path (`OrderService -> StockService` saga flow).

## What It Validates

- High-contention order creation against a single hot product
- All created orders eventually finalize (`Confirmed` or `Rejected`)
- No `PendingStock` orders remain for the run
- No negative stock
- `stock_reservations` count matches confirmed orders
- No unexpected DLQ backlog during nominal load

## Run

Start the stack first:

```powershell
docker compose up -d --build
```

Run the load validation wrapper:

```powershell
pwsh ./platform/performance/run-hot-product.ps1
```

Force Docker-based k6 (if local `k6` is not installed):

```powershell
pwsh ./platform/performance/run-hot-product.ps1 -UseDockerK6
```

Tune parameters:

```powershell
pwsh ./platform/performance/run-hot-product.ps1 -SeedQty 500 -Vus 100 -Iterations 1000 -OrderQty 1
```

## Files

- `k6/order-hot-product.js`: traffic generator
- `run-hot-product.ps1`: orchestration + invariant checks + queue checks
- `sql/order_invariants.sql`: query snippets for order-side checks
- `sql/stock_invariants.sql`: query snippets for stock-side checks
