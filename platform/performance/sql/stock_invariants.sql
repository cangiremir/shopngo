-- Replace :product_id with the hot product UUID and :confirmed_ids with a UUID list.

SELECT "ProductId", "AvailableQuantity", "UpdatedAtUtc"
FROM inventory_items
WHERE "ProductId" = :product_id::uuid;

-- Example:
-- SELECT COUNT(*) FROM stock_reservations WHERE "OrderId" IN ('uuid1'::uuid, 'uuid2'::uuid);
