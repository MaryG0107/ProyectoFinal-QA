-- =============================================================
-- FlashStore - Datos de prueba
-- Contraseñas:
--   admin@flashstore.test          -> Admin1234!
--   cliente1..cliente300@flashstore.test -> Test1234!
-- =============================================================

-- Administrador
INSERT INTO users (email, password_hash, full_name, role)
VALUES ('admin@flashstore.test', crypt('Admin1234!', gen_salt('bf', 10)), 'Administrador FlashStore', 'admin');

-- 300 clientes de prueba (para el escenario de las últimas 5 unidades).
-- Se calcula un solo hash y se reutiliza para que la carga inicial sea rápida.
WITH h AS (SELECT crypt('Test1234!', gen_salt('bf', 10)) AS hash)
INSERT INTO users (email, password_hash, full_name, role)
SELECT 'cliente' || n || '@flashstore.test', h.hash, 'Cliente de Prueba ' || n, 'customer'
FROM generate_series(1, 300) AS n, h;

-- Categorías
INSERT INTO categories (name) VALUES
    ('Videojuegos'),
    ('Celulares'),
    ('Computación'),
    ('Audio'),
    ('Hogar');

-- Productos
-- El producto 1 es el producto de la campaña flash: SOLO 5 UNIDADES.
INSERT INTO products (category_id, name, description, price, stock, is_featured) VALUES
    (1, 'Consola FlashBox X - Edición Limitada', 'Consola de edición limitada para la campaña flash. Solo 5 unidades disponibles.', 3999.00, 5, true),
    (1, 'Control inalámbrico FlashPad', 'Control inalámbrico con batería recargable.', 449.00, 120, false),
    (1, 'Juego Aventura Galáctica', 'Juego de aventura para consola FlashBox.', 399.00, 80, false),
    (2, 'Celular Nova 12 128GB', 'Pantalla de 6.5 pulgadas, 128 GB de almacenamiento.', 2799.00, 40, true),
    (2, 'Cargador rápido 30W', 'Cargador USB-C de carga rápida.', 149.00, 200, false),
    (3, 'Laptop Orion 14" 16GB RAM', 'Procesador de 8 núcleos, 16 GB de RAM y SSD de 512 GB.', 6499.00, 25, true),
    (3, 'Mouse inalámbrico Silent', 'Mouse silencioso con receptor USB.', 129.00, 150, false),
    (3, 'Teclado mecánico Spark', 'Teclado mecánico retroiluminado.', 499.00, 60, false),
    (4, 'Audífonos Bluetooth Wave', 'Audífonos con cancelación de ruido.', 899.00, 70, true),
    (4, 'Bocina portátil Boom', 'Bocina resistente al agua.', 549.00, 90, false),
    (5, 'Freidora de aire 4L', 'Freidora de aire con 8 programas.', 799.00, 35, false),
    (5, 'Licuadora Turbo 1.5L', 'Licuadora de 10 velocidades.', 459.00, 45, false);
