-- Deja el producto 1 con exactamente 5 unidades y borra los pedidos de pruebas anteriores.
-- Uso:  docker exec -i flashstore-postgres psql -U flashstore -d flashstore < tests/performance/reset-flash-sale.sql
BEGIN;
DELETE FROM idempotency_keys;
DELETE FROM payments;
DELETE FROM order_items;
DELETE FROM orders;
DELETE FROM notifications;
UPDATE products SET stock = 5, updated_at = now() WHERE id = 1;
COMMIT;
SELECT id, name, stock FROM products WHERE id = 1;
