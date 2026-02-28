-- Replace :run_like with a pattern such as 'k6+abc123-%@example.com'

SELECT COUNT(*) AS total_orders
FROM orders
WHERE "CustomerEmail" LIKE :run_like;

SELECT "Status", COUNT(*) AS count
FROM orders
WHERE "CustomerEmail" LIKE :run_like
GROUP BY "Status"
ORDER BY "Status";

SELECT COUNT(*) AS pending_orders
FROM orders
WHERE "CustomerEmail" LIKE :run_like
  AND "Status" = 'PendingStock';
